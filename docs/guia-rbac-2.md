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

## Cache de permisos (F2-09)

**Criterio de aceptación:** "Sin consulta SQL por request normal".

Antes de F2-09, `PermissionEvaluator` (F2-07) consultaba `IPermissionService.GetPermissionsForUserAsync`/
`GetPermissionsForRoleAsync` (SQL Server, vía `UserManager`/`RoleManager`) en CADA request autenticado, sin
ningún cache. F2-09 agrega esa pieza sin tocar `IPermissionEvaluator`/`PermissionEvaluator` ni el
comportamiento fail-closed ya probado de F2-07/F2-08: decora `IPermissionService` (la única parte de la
cadena que realmente toca SQL Server) con `CachedPermissionService`
(`Shared.Infrastructure.Security.Permissions`), reutilizando el mecanismo de cache L1 (memoria)/L2 (Redis)
ya existente del framework (`HybridCache`/`ITenantAwareCache`, F1-16) en vez de un mecanismo propio.

### Por qué se decora `IPermissionService` y no `IPermissionEvaluator`

La única parte costosa de `PermissionEvaluator.EvaluateAsync` es la consulta SQL de `IPermissionService`;
la combinación de fuentes (claims del token, narrowing por scope, validación de tenant) ya opera en
memoria. Decorar en esta capa, en vez del evaluador completo, además da la granularidad de invalidación
correcta: `GetPermissionsForUserAsync(userId)` y `GetPermissionsForRoleAsync(roleName)` son exactamente
las dos preguntas que cambian cuando se reasigna un rol a un usuario o se agrega/quita un permiso a un
rol — invalidar por esa clave es preciso, mientras que invalidar "el resultado de `EvaluateAsync` para
este `ClaimsPrincipal`" no tendría una clave natural (el principal no es un identificador estable entre
requests).

### Dos cachés, dos mecanismos

| Método | Mecanismo | Por qué |
|---|---|---|
| `GetPermissionsForUserAsync(userId)` | `ITenantAwareCache` (F1-16) | Los permisos de un usuario SÍ son datos sensibles a tenant (regla dura #14, `docs/convenciones.md`) — la clave compone el `TenantId` del scope actual. |
| `GetPermissionsForRoleAsync(roleName)` | `HybridCache` directo, sin prefijo de tenant | `ApplicationRole` todavía no implementa `ITenantEntity` (gap documentado más abajo): un rol creado en un tenant ya es visible/asignable en cualquier otro, así que sus permisos no son un dato sensible a tenant hoy. |

### TTL por defecto y cómo ajustarlo

`PermissionCacheOptions.Expiration` (default: **60 segundos**) es el tiempo de vida de una entrada
cacheada, tanto en L1 como en L2. Se configura con el delegado de `AddSharedPermissionCache`:

```csharp
services.AddSharedPermissionCache(options => options.Expiration = TimeSpan.FromSeconds(30));
```

Es el límite superior de "cuánto puede tardar en verse reflejado" un cambio de permisos que no pasó por
invalidación explícita (ver debajo) — un valor corto prioriza corrección sobre reducción de carga en SQL
Server; un valor largo hace lo contrario. No se lee de `IConfiguration` automáticamente (mismo criterio
que `AbacOptions`, F2-08): es un parámetro de afinación de infraestructura, no un dato de negocio por
tenant.

### Cómo y cuándo se invalida

`IPermissionCacheInvalidator` (`Shared.Infrastructure.Security.Permissions`) es la invalidación explícita
— nunca automática/declarativa, mismo criterio que `IAuthorizationPolicyEvaluator` (F2-08): Identity
expone las mutaciones de roles/permisos directamente vía `UserManager`/`RoleManager`, sin ningún punto de
extensión común que el framework pueda interceptar genéricamente.

```csharp
public interface IPermissionCacheInvalidator
{
    ValueTask InvalidateUserAsync(Guid userId, CancellationToken cancellationToken = default);
    ValueTask InvalidateRoleAsync(string roleName, CancellationToken cancellationToken = default);
}
```

Un proyecto consumidor debe invocar el método correspondiente inmediatamente después de:

- **Cambiar la asignación de roles de un usuario** (`UserManager.AddToRoleAsync`/`RemoveFromRoleAsync`, o
  cualquier otro cambio que afecte qué roles/permisos tiene ESE usuario) → `InvalidateUserAsync(userId)`.
- **Agregar o quitar un permiso a un rol** → `InvalidateRoleAsync(roleName)`. `RoleManagerPermissionExtensions`
  (F2-07) agrega en esta tarea `RemovePermissionAsync` (simétrico de `AddPermissionAsync`, ausente hasta
  ahora) y un overload de cada uno que recibe un `IPermissionCacheInvalidator` e invalida automáticamente
  si la operación tuvo éxito:

```csharp
await roleManager.AddPermissionAsync(role, "productos.crear", invalidator, ct);
await roleManager.RemovePermissionAsync(role, "productos.crear", invalidator, ct);

// Alta/baja de rol de un usuario: Identity no tiene un overload propio -- invalidar explícitamente.
await userManager.AddToRoleAsync(user, "Ventas");
await invalidator.InvalidateUserAsync(user.Id, ct);
```

**Sin invalidación explícita, la entrada cacheada sigue vigente hasta `PermissionCacheOptions.Expiration`**
— nunca indefinidamente (fail-closed acotado), pero tampoco de forma inmediata. `IPermissionCacheInvalidator`
se registra por defecto como `NullPermissionCacheInvalidator` (no-op, vía `AddSharedPermissionEvaluation`):
un proyecto puede inyectarlo siempre, esté o no habilitado el cache, sin condicionar su código — mismo
patrón que `NullPermissionService`/`NullTenantProvider`.

`InvalidateRoleAsync` **no invalida en cascada** los permisos efectivos ya cacheados de los usuarios que
tienen ese rol (rastrear la membresía rol→usuarios de forma eficiente no lo expone Identity, y esta tarea
no lo incorpora): ese caso queda acotado únicamente por el TTL. Es una decisión consciente, no un
descuido — invalidar solo la clave directamente afectada (el rol, o el usuario concreto) cubre el caso
común (asignar/quitar un rol a un usuario puntual) con invalidación inmediata, y acepta una ventana
acotada y documentada para el caso menos común (cambiar los permisos de un rol con muchos usuarios).

### Límite de correctitud en despliegues multi-instancia

`HybridCache` no propaga la invalidación de la capa L1 (memoria) entre instancias del proceso:
`RemoveAsync` borra la entrada L2 (Redis) y la copia L1 de la instancia que invoca, pero la copia L1 de
OTRA instancia sigue vigente hasta que expire por `PermissionCacheOptions.Expiration` — mismo límite ya
documentado para `ITenantAwareCache` (F1-16: "la escritura a Redis L2 es asíncrona, no asumir consistencia
inmediata entre instancias"). Esto fija el techo real de staleness de un permiso revocado en un despliegue
de más de una instancia: nunca indefinido, pero tampoco inmediato en todas las instancias. Un proyecto que
necesite un techo más bajo ajusta `PermissionCacheOptions.Expiration` en consecuencia.

### Registro

`AddSharedPermissionCache(Action<PermissionCacheOptions>? configureOptions = null)`
(`Shared.Infrastructure.Security.Permissions`) debe llamarse DESPUÉS de `AddSharedSecurity`/
`AddSharedOidcAuthentication` (o, como mínimo, `AddSharedPermissionEvaluation`) — decora el
`IPermissionService` ya registrado; sin ese registro previo, lanza `InvalidOperationException` en el
arranque (nunca en tiempo de request):

```csharp
services.AddSharedSecurity<ApplicationUser, ApplicationRole, MiDbContext>(configuration); // RBAC, F2-07
services.AddSharedPermissionCache(); // F2-09, TTL 60s por defecto
```

Es idempotente (una segunda llamada no vuelve a envolver el `IPermissionService` ya decorado) y registra
`ITenantAwareCache`/`HybridCache` si el proyecto todavía no llamó a `AddSharedCaching` por su cuenta.
`IPermissionCacheInvalidator` resuelve a la MISMA instancia que `IPermissionService` dentro del scope
(ambos son el mismo `CachedPermissionService`).

### Orden de llamada: `AddSharedPermissionCache` NUNCA antes de `AddSharedSecurity`

El orden "DESPUÉS de `AddSharedSecurity`" de arriba no es solo una recomendación de estilo: invertirlo
deja el cache sin ningún efecto, de forma silenciosa. `AddSharedSecurity` hace
`services.AddScoped<IPermissionService, PermissionService<TUser, TRole>>()` -- un `Add` simple, no un
`Replace` -- así que si se llama DESPUÉS de `AddSharedPermissionCache`, agrega un descriptor de
`IPermissionService` que gana la resolución sobre el `Replace` que ya había hecho el cache, y
`CachedPermissionService` queda huérfano (nadie lo resuelve) sin ningún error visible en el arranque:

```csharp
// INCORRECTO -- compila, arranca, y el cache NUNCA se usa (huérfano):
services.AddSharedPermissionEvaluation();
services.AddSharedPermissionCache();
services.AddSharedSecurity<ApplicationUser, ApplicationRole, MiDbContext>(configuration);
```

Para el caso concreto de invertir el orden respecto de `AddSharedSecurity`, `AddSharedSecurity` detecta
en su propio registro que `AddSharedPermissionCache` ya decoró `IPermissionService` (mismo criterio de
idempotencia que usa `AddSharedPermissionCache`, expuesto internamente como
`IsPermissionCacheAlreadyApplied`) y lanza `InvalidOperationException` de inmediato en el arranque, con
el mensaje indicando el reordenamiento correcto -- en vez de dejar el bug pasar desapercibido hasta que
alguien note en producción que el cache "no cachea".

Esta detección NO puede vivir en `AddSharedPermissionCache` en el momento en que se lo invoca: llamarlo
justo después de `AddSharedPermissionEvaluation` y ANTES de `AddSharedSecurity` es indistinguible, en ese
instante, del caso legítimo de un proyecto solo-OIDC (`AddSharedOidcAuthentication` sin Identity local)
que decide "cachear" un `NullPermissionService` que nunca se va a reemplazar -- un no-op inofensivo que
varias pruebas de este archivo ejercitan a propósito (`PermissionCacheServiceCollectionExtensionsTests`).
Por eso el chequeo se hace del lado de `AddSharedSecurity`, que es el único punto donde el framework sabe
con certeza que la implementación real de RBAC está llegando tarde.

## Qué NO resuelve F2-07 (alcance de tareas posteriores de la Épica F2-B)

- **F2-08 (ABAC):** implementado. `IAuthorizationPolicyEvaluator` (`Shared.Infrastructure.Security.Abac`)
  combina el permiso RBAC de este evaluador con reglas de negocio basadas en atributos del recurso
  (`Subject`/`Resource`/`Action`/`Context`, monto/empresa/sucursal) — ver `docs/guia-abac.md`.
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
- `tests/Shared.Infrastructure.Security.Tests/CachedPermissionServiceTests.cs` (F2-09): prueba de
  componente contra `HybridCache` REAL (L1 en memoria, sin mocks) -- solo `IPermissionService` está
  sustituido. Cubre hit/miss para usuario y para rol, aislamiento de cache entre usuarios distintos,
  aislamiento de cache entre tenants distintos para el mismo `userId` (y que `InvalidateUserAsync` de un
  tenant no afecta al otro), que el cache de rol SÍ se comparte entre tenants (decisión de diseño
  documentada arriba), expiración por TTL para ambos métodos, e invalidación explícita para ambos
  métodos.
- `tests/Shared.Infrastructure.Security.Tests/Integration/CachedPermissionServiceRedisIntegrationTests.cs`
  (Testcontainers, Redis real): mismo criterio que `HybridCacheRedisIntegrationTests` (F1-16) -- dos
  `ServiceProvider`/`HybridCache` independientes (dos instancias del proceso) comparten el valor
  cacheado a través de Redis L2, para `GetPermissionsForUserAsync` y `GetPermissionsForRoleAsync`; y que
  `InvalidateUserAsync` borra realmente la entrada de Redis (una instancia nueva, con L1 vacía, recalcula
  en vez de leer el valor viejo).
- `tests/Shared.Infrastructure.Security.Tests/PermissionCacheServiceCollectionExtensionsTests.cs`: el
  registro DI (falla sin `IPermissionService` previo, decora correctamente, `IPermissionService`/
  `IPermissionCacheInvalidator` resuelven a la misma instancia, aplica el delegado de opciones, es
  idempotente, decora también un `IPermissionService` registrado directamente por el proyecto).
- `tests/Shared.Infrastructure.Security.Tests/RoleManagerPermissionExtensionsTests.cs`: `RemovePermissionAsync`
  (nuevo, simétrico de `AddPermissionAsync`) y los overloads con `IPermissionCacheInvalidator` de ambos
  métodos.
- `tests/Shared.Infrastructure.Security.Tests/NullPermissionCacheInvalidatorTests.cs`: el no-op.
- `tests/Shared.Infrastructure.Security.Tests/SecurityServiceCollectionExtensionsTests.cs`: `AddSharedSecurity`
  registra `NullPermissionCacheInvalidator` por defecto cuando no se llamó a `AddSharedPermissionCache`;
  además (fix post-revisión de arquitectura de F2-09) `AddSharedSecurity` lanza `InvalidOperationException`
  si se lo llama DESPUÉS de `AddSharedPermissionCache` (orden invertido, decorador huérfano) y resuelve el
  `CachedPermissionService` correctamente cuando el orden es el documentado.

## Referencias

- `src/Shared.Infrastructure.Security/Permissions/` — `IPermissionEvaluator`/`PermissionEvaluator`,
  `PermissionGrant`/`PermissionGrantSources`/`EffectivePermissions`, `ScopeClaimTypes`,
  `NullPermissionService`, `PermissionEvaluationServiceCollectionExtensions`,
  `CachedPermissionService`/`IPermissionCacheInvalidator`/`NullPermissionCacheInvalidator`/
  `PermissionCacheOptions`/`PermissionCacheServiceCollectionExtensions` (F2-09).
- `docs/adr/0006-cache-hybridcache-valkey-redis.md` — mecanismo de cache reutilizado por F2-09
  (`HybridCache`/`ITenantAwareCache`, F1-16) en vez de un mecanismo propio.
- `src/Shared.Infrastructure.Security/Oidc/OidcRoleClaimsTransformation.cs` y
  `OidcOptions.RoleClaimJsonPaths` — proyección de roles anidados (Keycloak `realm_access.roles`) a
  `ClaimTypes.Role`, registrada por `AddSharedOidcAuthentication` (bugfix de correctitud de F2-07).
- `docs/guia-oidc-adapter.md` — adapter OIDC/OAuth2 (F2-01 a F2-06), la Épica F2-A completa sobre la que
  se apoya F2-07.
- `docs/convenciones.md` — reglas duras del framework, y la entrada "proteger un endpoint por permiso".
- `docs/plan-maestro-bitcode-ia.md` — backlog completo de la Épica F2-B (F2-07 a F2-11).
