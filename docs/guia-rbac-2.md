# Guía — RBAC 2.0: evaluador de permisos efectivos (F2-07)

**Tarea:** F2-07 (Fase 2, Épica F2-B — Autorización) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Criterio de aceptación:** "Permisos efectivos trazables".

## Qué resuelve esta tarea

Antes de F2-07, la única forma de saber qué podía hacer un sujeto autenticado era llamar a
`IPermissionService.GetPermissionsForUserAsync(userId)` (F1): expandía los roles de
[ASP.NET Core Identity](https://learn.microsoft.com/aspnet/core/security/authentication/identity) de
un `ApplicationUser` local a la unión de sus permisos (claims `"permission"` sobre
`AspNetRoleClaims`), sin distinguir de qué rol vino cada uno y sin ninguna forma de funcionar para una
identidad autenticada por un IdP externo (F2-01, `AddSharedOidcAuthentication`) que no tuviera un
`ApplicationUser` asociado en la base local — de hecho, `AddSharedOidcAuthentication` ni siquiera
registraba un `IAuthorizationPolicyProvider` dinámico, así que `[RequirePermission]`/
`RequireAuthorization("permiso")` no tenían ningún efecto bajo autenticación puramente OIDC (gap
documentado explícitamente en `docs/guia-oidc-adapter.md`, sección de coexistencia con el JWT propio).

F2-07 normaliza el modelo de roles/permisos/scopes/tenancy en un único evaluador
(`IPermissionEvaluator`) que:

1. Funciona igual bajo `AddSharedSecurity` (JWT propio) y `AddSharedOidcAuthentication` (F2-01) — ambos
   métodos registran ahora la misma infraestructura de autorización dinámica
   (`AddSharedPermissionEvaluation`, `Shared.Infrastructure.Security.Permissions`).
2. Devuelve un resultado trazable (`EffectivePermissions.Grants`, una lista de
   `PermissionGrant(Permission, Source)`): ante una pregunta de auditoría ("¿por qué este sujeto puede
   ejercer este permiso?") alcanza con inspeccionar `Source`, sin reconstruir manualmente qué rol o
   claim del token lo originó.
3. Incorpora el scope OAuth2 del token (RFC 6749 sección 3.3) como límite adicional sobre los permisos
   RBAC, y valida que el tenant declarado en el token (si lo trae) coincida con el tenant ya resuelto
   para el request — ambos fail-closed.

## `IPermissionEvaluator` — el evaluador normalizado

```csharp
public interface IPermissionEvaluator
{
    Task<EffectivePermissions> EvaluateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}
```

`PermissionEvaluator` (implementación por defecto) combina tres fuentes, en este orden:

1. **Roles de Identity local**, si el principal trae un `ClaimTypes.NameIdentifier` que resuelve a un
   `ApplicationUser` conocido: `IPermissionService.GetPermissionsForUserAsync(userId)` (comportamiento
   heredado de F1, sin cambios). Cada permiso queda trazado con `PermissionGrantSources.LocalIdentityRoles`.
2. **Nombres de rol como claim del token, cuando la fuente 1 no produjo ningún permiso** (identidad
   puramente externa, F2-01, O un userId local que `IPermissionService.GetPermissionsForUserAsync` no
   supo resolver): cada valor de `ClaimTypes.Role` presente en el token se expande contra los roles
   conocidos localmente vía el método `IPermissionService.GetPermissionsForRoleAsync(roleName)` — así un
   IdP externo que emita nombres de rol ya administrados por el framework también autoriza por permiso,
   sin depender de que el sujeto tenga una fila en la tabla de usuarios local. Cada permiso queda
   trazado con `PermissionGrantSources.FromRoleClaim(roleName)`.

   **Bugfix de correctitud (posterior a la entrega inicial de F2-07):** este fallback se activa por "la
   fuente 1 no produjo ningún permiso", NO por "el principal no tiene claim `NameIdentifier`". La
   versión original de esta tarea usaba ese segundo criterio, y resultaba incorrecta en la práctica:
   `JwtSecurityTokenHandler` mapea automáticamente el claim estándar `"sub"` del token a
   `ClaimTypes.NameIdentifier`, y el `sub` de la inmensa mayoría de los IdP OIDC (Keycloak incluido) es
   un UUID — con forma idéntica a un `Guid` válido. El resultado: **cualquier** identidad puramente
   externa autenticada contra Keycloak (sin `ApplicationUser` local) tomaba por error la rama "hay
   userId local", consultaba `NullPermissionService` (vacío por diseño, nunca aporta permisos) y NUNCA
   llegaba a expandir sus roles por nombre — pese a que el criterio de aceptación de F2-07 es
   justamente que esto funcione. Corregido: la fuente 1 se evalúa igual (si el `NameIdentifier` parsea
   como `Guid`), pero si no produjo ningún grant, el evaluador cae al camino de expansión por rol en vez
   de devolver vacío.

   Además, para que este camino tenga algo que expandir contra Keycloak, **el `ClaimTypes.Role` debe
   existir primero** en el `ClaimsPrincipal`: Keycloak no emite un claim de rol plano, sino los roles de
   realm anidados como `realm_access.roles` (un array JSON dentro de un único claim), que
   `JwtBearerHandler` nunca aplana automáticamente. `AddSharedOidcAuthentication` (F2-01) registra para
   esto `Oidc.OidcRoleClaimsTransformation` (`IClaimsTransformation`), que proyecta esos roles anidados
   como `ClaimTypes.Role` ANTES de que el pipeline de autorización (y por lo tanto `PermissionEvaluator`)
   vea el principal. Las rutas de claim a resolver son configurables
   (`OidcOptions.RoleClaimJsonPaths`, default `["realm_access.roles"]`) — agnósticas de proveedor
   concreto: cada entrada tiene la forma `"claimSuperior.propiedad[.propiedadAnidada...]"`, resuelta
   genéricamente navegando el JSON del claim superior, sin ningún nombre de cliente/proveedor
   hardcodeado en el código. Para incluir también roles de un cliente Keycloak concreto, agregar
   `"resource_access.<client-id>.roles"` a esa lista por configuración (el nombre del cliente varía por
   despliegue, por eso no está en el default).
3. **Permisos declarados directamente como claim del token** (`PermissionClaimTypes.Permission`,
   `"permission"`): un IdP externo puede emitir el permiso literal sin pasar por ningún concepto de rol.
   Trazado con `PermissionGrantSources.TokenPermissionClaim`.

Las fuentes 1 y 2 son mutuamente excluyentes por request (según haya o no userId local reconocible); la
fuente 3 siempre se evalúa y se suma a cualquiera de las dos anteriores.

### Narrowing por scope OAuth2

Si el token trae un claim `scope` (`ScopeClaimTypes.Scope`, espacio-separado, RFC 6749 sección 3.3) con
**al menos un valor con forma de permiso** (contiene un punto, `"{entidad}.{accion}"` — ver
`docs/convenciones.md`), esos scopes actúan como lista explícita de permisos autorizados para ESE
token: un permiso que el rol del sujeto tendría en general, pero que no figura entre los scopes
declarados, no se concede para esa llamada. Es el mecanismo pensado para un token de Client Credentials
(F2-04) emitido a propósito con un scope reducido. Un scope sin forma de permiso (`openid`, `profile`,
`email`, u otro sin punto — los scopes típicos de un login interactivo, F2-02) se ignora para este
propósito: **no hay narrowing si ningún scope declarado tiene forma de permiso**, así que un usuario
final autenticado por Authorization Code + PKCE (F2-02, scope por defecto `"openid profile email"`)
sigue recibiendo el conjunto completo de sus permisos de rol, sin verse afectado por este mecanismo.

### Consistencia de tenant

Si el proyecto opera en modo multi-tenant (`ITenantContext.IsMultiTenancyEnabled`) y ya hay un tenant
resuelto para el request (`ITenantContext.TenantId`), y el token trae un claim
`TenantClaimTypes.TenantId` (`"tenant_id"`, el mismo que emite `JwtTokenGenerator` para el JWT propio,
F1-12) que **no coincide** con el tenant resuelto, el resultado es `EffectivePermissions.Empty` —
default deny, nunca se evalúan permisos con un tenant inconsistente. Si el token no trae ese claim (por
ejemplo, un IdP externo sin ese claim propietario), no se bloquea por ausencia: no hay con qué
contrastar.

## Ejemplo de uso — trazabilidad de un permiso efectivo

```csharp
public class AuditoriaPermisosEndpoint
{
    public static async Task<IResult> Handle(
        ClaimsPrincipal user,
        IPermissionEvaluator evaluator,
        CancellationToken ct)
    {
        var effective = await evaluator.EvaluateAsync(user, ct);

        // effective.Grants: [ { Permission = "productos.crear", Source = "identity-local:roles" },
        //                     { Permission = "pedidos.reservar-stock", Source = "token:permission-claim" } ]
        return Results.Ok(effective.Grants);
    }
}
```

## Registro

`AddSharedPermissionEvaluation()` (`Shared.Infrastructure.Security.Permissions`) registra:

- `ITenantProvider`/`ITenantContext` con `TryAddScoped` (mismo fallback `NullTenantProvider`/
  `TenantContext` que `AddSharedPersistence`, F1-12/F1-15) — para que el evaluador pueda validar
  tenancy incluso en un proyecto que todavía no llamó a `AddSharedPersistence`.
- `IPermissionService` con `TryAddScoped` sobre `NullPermissionService` (nunca aporta permisos por sí
  sola, pero deja que el evaluador siga funcionando con las fuentes 2 y 3 de arriba).
- `IPermissionEvaluator` → `PermissionEvaluator` (`TryAddScoped` desde F2-08 — ver nota de idempotencia
  más abajo).
- `IAuthorizationHandler` → `PermissionAuthorizationHandler` (`TryAddEnumerable` desde F2-08; depende de
  `IPermissionEvaluator`, no de `IPermissionService` directamente).
- `IAuthorizationPolicyProvider` → `PermissionAuthorizationPolicyProvider` (sin cambios, F1).
- `AddAuthorization()`.

Tanto `AddSharedSecurity<TUser,TRole,TContext>` (JWT propio) como `AddSharedOidcAuthentication` (F2-01)
llaman a `AddSharedPermissionEvaluation()`:

- `AddSharedSecurity` la llama y **además** registra `IPermissionService` real
  (`PermissionService<TUser,TRole>`, respaldado por `UserManager`/`RoleManager` de Identity), que
  reemplaza al `NullPermissionService` (el `AddScoped` explícito, sin `TryAdd`, se registra después y
  gana la resolución).
- `AddSharedOidcAuthentication` la llama sin registrar ningún `IPermissionService` propio: un proyecto
  solo-OIDC sigue pudiendo usar `[RequirePermission]` contra los permisos/roles que el propio IdP
  declare como claim del token (fuentes 2 y 3 de arriba), sin necesitar Identity local.

```csharp
// Proyecto con Identity local (JWT propio o mixto).
services.AddSharedSecurity<ApplicationUser, ApplicationRole, MiDbContext>(configuration);

// Proyecto solo-OIDC, sin Identity local: [RequirePermission] ya funciona contra los claims del
// propio IdP externo (roles/permission/scope), sin ningún registro adicional.
services.AddSharedOidcAuthentication(configuration);
```

**Nota de idempotencia (F2-08):** `AddSharedPermissionEvaluation` es seguro de llamar más de una vez
sobre el mismo `IServiceCollection` (`TryAddScoped`/`TryAddEnumerable`, no `AddScoped` — cambio puntual de
F2-08, ver `docs/guia-abac.md`). Esto es lo que permite que `AddSharedAbacAuthorization` (F2-08) también
la llame para garantizar `IPermissionEvaluator` disponible, sin duplicar el registro de
`IAuthorizationHandler` cuando un proyecto combina RBAC + ABAC en el mismo `IServiceCollection`.

## Qué NO resuelve F2-07 (alcance de tareas posteriores de la Épica F2-B)

- **F2-08 (ABAC):** implementado. `IAuthorizationPolicyEvaluator` (`Shared.Infrastructure.Security.Abac`)
  combina el permiso RBAC de este evaluador con reglas de negocio basadas en atributos del recurso
  (`Subject`/`Resource`/`Action`/`Context`, monto/empresa/sucursal) — ver `docs/guia-abac.md`.
- **F2-09 (cache de permisos):** `IPermissionEvaluator`/`IPermissionService` siguen consultando SQL
  Server (vía `UserManager`/`RoleManager`) en cada evaluación — no hay L1/L2 todavía. No usar el
  resultado de `EvaluateAsync` como si estuviera cacheado entre requests.
- **F2-10 (operaciones privilegiadas):** step-up, reevaluación y segregación de funciones no están
  cubiertos por el evaluador de F2-07.
- **F2-11 (pruebas de autorización):** la matriz allow/deny y las pruebas de bypass de la épica
  completa son una tarea separada; F2-07 solo prueba el evaluador en sí (ver más abajo).
- **Roles tenant-scoped:** `ApplicationRole` (Identity) sigue sin implementar `ITenantEntity` — un rol
  creado en un tenant sigue siendo visible/asignable en cualquier otro tenant del mismo proyecto. Es
  un cambio de esquema del modelo multi-tenant (Plan Maestro, sección 13: requiere aprobación humana
  explícita) que F2-07 no incluye; la defensa que sí agrega esta tarea es la validación de
  consistencia de tenant descrita arriba (a nivel de evaluación de permisos, no de esquema de roles).

## Pruebas

- `tests/Shared.Infrastructure.Security.Tests/PermissionEvaluatorTests.cs`: las tres fuentes de
  permisos (roles de Identity local, roles por claim sin userId local, permiso directo del token), el
  narrowing por scope (con y sin forma de permiso), los dos casos de consistencia de tenant (claim
  ausente no bloquea; claim presente y distinto del tenant resuelto sí bloquea), el fallback por
  "userId con forma de GUID pero no reconocido localmente" (`EvaluateAsync_NameIdentifierIsGuidShapedButUnknownLocally_FallsBackToRoleClaimExpansion`,
  el bugfix) y un caso que ejercita `OidcRoleClaimsTransformation` + `PermissionEvaluator` juntos a
  partir de un claim `realm_access` con forma real de Keycloak (no un `ClaimTypes.Role` ya armado a
  mano).
- `tests/Shared.Infrastructure.Security.Tests/Oidc/OidcRoleClaimsTransformationTests.cs`: proyección de
  `realm_access.roles`/`resource_access.<client>.roles` a `ClaimTypes.Role` a partir de JSON con forma
  real de Keycloak, incluyendo los casos negativos (claim ausente, JSON inválido, sin propiedad
  `roles`, rol duplicado no se repite).
- `tests/Shared.Infrastructure.Security.Tests/Integration/OidcRoleClaimsIntegrationTests.cs`
  (Testcontainers, Keycloak real): crea un rol de realm real, lo asigna a la cuenta de servicio del
  cliente de prueba, obtiene un access token real y verifica que `[RequirePermission]` concede (y, en
  el control negativo, deniega) el permiso mapeado a ese rol — la única forma de probar de verdad, sin
  mocks, que el bugfix funciona contra el IdP real. Reutiliza `KeycloakContainerFixture`/
  `KeycloakCollection` (F2-05); el fixture ganó `AssignRealmRoleToServiceAccountAsync` y su realm de
  prueba declara explícitamente el protocol mapper de roles de realm (necesario porque el realm export
  mínimo no adjunta el client scope built-in `"roles"` por defecto).
- `tests/Shared.Infrastructure.Security.Tests/PermissionAuthorizationHandlerTests.cs`: actualizado
  para depender de `IPermissionEvaluator` (antes dependía directamente de `IPermissionService`).
- `tests/Shared.Infrastructure.Security.Tests/PermissionServiceTests.cs`: casos nuevos para
  `GetPermissionsForRoleAsync` (rol existente y rol inexistente).
- `tests/Shared.Infrastructure.Security.Tests/SecurityServiceCollectionExtensionsTests.cs`: `AddSharedSecurity`
  resuelve `IPermissionService` como `PermissionService<TUser,TRole>` real (no el `NullPermissionService`
  de fallback) y expone `IPermissionEvaluator`.
- `tests/Shared.Infrastructure.Security.Tests/OidcAuthenticationServiceCollectionExtensionsTests.cs`:
  `AddSharedOidcAuthentication` registra `IPermissionEvaluator`/`IAuthorizationPolicyProvider`/
  `IAuthorizationHandler` sin necesitar `AddSharedSecurity` (el gap que motivó F2-07).

## Referencias

- `src/Shared.Infrastructure.Security/Permissions/` — `IPermissionEvaluator`/`PermissionEvaluator`,
  `PermissionGrant`/`PermissionGrantSources`/`EffectivePermissions`, `ScopeClaimTypes`,
  `NullPermissionService`, `PermissionEvaluationServiceCollectionExtensions`.
- `src/Shared.Infrastructure.Security/Oidc/OidcRoleClaimsTransformation.cs` y
  `OidcOptions.RoleClaimJsonPaths` — proyección de roles anidados (Keycloak `realm_access.roles`) a
  `ClaimTypes.Role`, registrada por `AddSharedOidcAuthentication` (bugfix de correctitud de F2-07).
- `docs/guia-oidc-adapter.md` — adapter OIDC/OAuth2 (F2-01 a F2-06), la Épica F2-A completa sobre la que
  se apoya F2-07.
- `docs/convenciones.md` — reglas duras del framework, y la entrada "proteger un endpoint por permiso".
- `docs/plan-maestro-bitcode-ia.md` — backlog completo de la Épica F2-B (F2-07 a F2-11).
