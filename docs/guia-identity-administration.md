# Identity Administration — Fase 6, módulo 1

**Tarea:** módulo 1 (orden de implementación, tabla de la sección "Fase 6 — Plataforma funcional empresarial") del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Dependencia declarada:** Security 2.0 (Fase 2, `Shared.Infrastructure.Security`) — ya cerrada.
**Estado:** vertical slice real de 3 de las 5 capacidades mínimas (Usuarios, Roles/Permisos, Sesiones). Delegaciones queda pendiente explícito (ver "Pendientes").

Identity Administration es la capa de **administración** (CRUD de usuarios/roles/permisos vía API, con auditoría y RBAC/ABAC sobre esas operaciones) construida sobre la infraestructura de identidad/autenticación/autorización de bajo nivel que Security 2.0 (Fase 2) ya resolvió — no reimplementa JWT, RBAC 2.0, ABAC ni auditoría inmutable, los reutiliza.

## Ubicación

- Librería: `src/Platform/BitCode.Platform.Identity/` (proyecto `BitCode.Platform.Identity`, namespace `BitCode.Framework.Platform.Identity` — ver `docs/convenciones.md`, sección "Namespaces de módulos de Platform").
- Aplicación de referencia: `samples/Sample.IdentityAdmin.Api` (+ `samples/Sample.IdentityAdmin.Api.Tests`).

## Capacidades

| Capacidad | Estado | Notas |
|---|---|---|
| Usuarios | Completa | Alta, obtención, listado paginado, desactivación (bloqueo de login, no elimina datos) |
| Roles y permisos | Completa | Alta de rol, listado, conceder/quitar permiso (reutiliza `RoleManagerPermissionExtensions`, F2-09) |
| Sesiones | Completa (con límite documentado) | Listado/revocación de `RefreshToken` (Security 2.0) por usuario — no hay endpoint de login/emisión de token en este módulo, eso sigue siendo Security 2.0 |
| Delegaciones | **Pendiente** | Ver "Pendientes" |

## Modelo de datos

`IdentityAdministrationDbContext : MultiTenantIdentityDbContext<ApplicationUser, ApplicationRole>` (Security 2.0) es el dueño exclusivo del esquema de identidad: `AspNetUsers`/`AspNetRoles`/`AspNetUserRoles`/`AspNetUserClaims`/`AspNetRoleClaims`/`RefreshTokens`, más `IdempotencyKeys`/`OutboxMessages` (ya configurados por la clase base). Ningún otro módulo de plataforma debe leer/escribir estas tablas directamente — solo a través de los contratos públicos de este módulo o de los servicios de Security 2.0 ya expuestos (`UserManager<ApplicationUser>`/`RoleManager<ApplicationRole>`/`IPermissionService`).

El módulo usa los tipos concretos `ApplicationUser`/`ApplicationRole` de Security 2.0 directamente (sin genéricos propios) — decisión deliberada de simplicidad para el primer corte; un consumidor que necesite campos adicionales de usuario/rol extendería ese modelo en una tarea posterior.

## Cómo consumirlo desde un host

Orden de registro en `InfrastructureModule` del host (ver `samples/Sample.IdentityAdmin.Api/InfrastructureModule.cs` como referencia completa):

```csharp
// 1. Persistencia del módulo (DbContext + interceptores de auditoría/soft-delete/tenant/outbox +
//    IUnitOfWork/IIdempotencyStore/IInboxStore) -- antes de AddSharedSecurity.
services.AddSharedIdentityAdministrationPersistence(connectionString);

// 2. Identity/JWT/RBAC 2.0 (Security 2.0, F2-07) sobre ese mismo DbContext.
services.AddSharedSecurity<ApplicationUser, ApplicationRole, IdentityAdministrationDbContext>(configuration);

// 3. ABAC (F2-08) -- requerido por AsignarRolAUsuarioCommandHandler (SelfRoleAssignmentAbacRule).
services.AddSharedAbacAuthorization();

// 4. Auditoría inmutable (F2-15).
services.AddSharedAuditing();

// 5. Servicios propios del módulo (resolución de actor, la regla ABAC de no-autoasignación, el store
//    de sesiones).
services.AddSharedIdentityAdministration(configuration);

// 6. Idempotencia end-to-end (F1-22) -- header "Idempotency-Key" -- ANTES de AddSharedApplication.
services.AddHttpContextIdempotencyKeyProvider();

// 7. MediatR/FluentValidation deben escanear TAMBIÉN el ensamblado de la librería (los handlers viven
//    ahí, no en el host).
services.AddSharedApplication(typeof(InfrastructureModule).Assembly, typeof(IdentityAdministrationDbContext).Assembly);
```

Y en un módulo propio del host (no de la librería — ver el porqué en `docs/convenciones.md`):

```csharp
[DependsOn(typeof(InfrastructureModule))]
public class IdentityAdminModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

    public void ConfigureApplication(WebApplication app) => app.MapIdentityAdministrationEndpoints();
}
```

## Endpoints (`/api/v1/identidad/...`)

| Método | Ruta | Permiso RBAC | ABAC adicional |
|---|---|---|---|
| POST | `/usuarios` | `identidad.usuarios.crear` | — |
| GET | `/usuarios/{id}` | `identidad.usuarios.ver` | — |
| GET | `/usuarios?page=&pageSize=` | `identidad.usuarios.ver` | — |
| POST | `/usuarios/{id}/desactivar` | `identidad.usuarios.desactivar` | — |
| POST | `/usuarios/{id}/roles` | `identidad.usuarios.roles.asignar` | `SelfRoleAssignmentAbacRule` (no auto-asignación) |
| GET | `/usuarios/{id}/sesiones` | `identidad.sesiones.ver` | — |
| POST | `/roles` | `identidad.roles.crear` | — |
| GET | `/roles` | `identidad.roles.ver` | — |
| POST | `/roles/{nombreRol}/permisos` | `identidad.roles.permisos.administrar` | — |
| DELETE | `/roles/{nombreRol}/permisos/{permiso}` | `identidad.roles.permisos.administrar` | — |
| POST | `/sesiones/{id}/revocar` | `identidad.sesiones.revocar` | — |

Todas las mutaciones (`POST`/`DELETE`) implementan `IIdempotentCommand` (F1-22): requieren el header `Idempotency-Key`; sin él responden 400 `Idempotency.KeyRequired`.

## RBAC + ABAC en la asignación de roles (requisito común de Fase 6)

`identidad.usuarios.roles.asignar` es un permiso RBAC **distinto** de `identidad.usuarios.ver` (requisito común de Fase 6: "asignar un rol de administrador requiere un permiso distinto al de ver el listado de usuarios"). Además, `AsignarRolAUsuarioCommandHandler` evalúa explícitamente `IAuthorizationPolicyEvaluator` (F2-08) con la regla propia `SelfRoleAssignmentAbacRule`: un actor con el permiso RBAC de todos modos **no puede asignarse un rol a sí mismo** — separación de funciones mínima sobre una operación sensible (escalada de privilegios). Verificado end-to-end en `IdentityAdministrationEndpointsIntegrationTests.AsignarRol_ASiMismo_EsDenegadoPorAbacAunqueTengaElPermisoRbac` (403, contra SQL Server real).

Requiere que el host haya llamado `AddSharedAbacAuthorization()` — sin eso, `AsignarRolAUsuarioCommandHandler` falla al resolver `IAuthorizationPolicyEvaluator` (fail-fast en el arranque del contenedor DI, no en runtime).

## Auditoría

Cada mutación (`identidad.usuarios.crear`, `identidad.usuarios.desactivar`, `identidad.usuarios.roles.asignar`, `identidad.roles.crear`, `identidad.roles.permission.grant`/`revoke` — vía `RoleManagerPermissionExtensions`, `identidad.sesiones.revocar`) escribe una entrada vía `IAuditWriter` (F2-15), incluidas las denegaciones ABAC de auto-asignación (`AuditOutcome.Denied`). Un fallo de escritura de auditoría nunca bloquea la operación de negocio ya resuelta (mismo criterio que el resto del framework, F2-15/F2-10).

## Pendientes explícitos

1. **Delegaciones** — no implementadas en este primer corte. Requiere diseño propio (entidad `Delegacion`: delegador, delegado, alcance, vigencia) y una decisión de cómo interactúa con la resolución de permisos existente (¿el delegado hereda permisos del delegador temporalmente, o son claims propios con vencimiento?) — se sugiere abrir un ADR antes de implementarla, dado que toca el modelo de RBAC 2.0 ya cerrado en Fase 2.
2. **Eventos de dominio/integración** — `ApplicationUser`/`ApplicationRole` (Security 2.0) no heredan de `AggregateRoot<TId>` (Shared.Kernel): no pueden usar `RaiseDomainEvent`, así que `OutboxSaveChangesInterceptor` (F1-23) no captura ningún evento de este módulo hoy. Este primer corte NO publica `UsuarioCreado`/`RolAsignado` como eventos de integración — requisito común 4 de Fase 6 ("Eventos de dominio e integración") queda parcialmente incumplido. Alternativas a evaluar en una tarea futura: (a) que `ApplicationUser`/`ApplicationRole` empiecen a implementar `IHasDomainEvents` directamente (cambio en Security 2.0, Fase 2, requiere análisis de compatibilidad), o (b) que los handlers de este módulo agreguen filas `OutboxMessage` explícitamente (rompe la regla implícita de que solo el interceptor las escribe, documentada en `docs/guia-outbox-publisher.md`) — ninguna de las dos se adoptó sin antes discutirlo (ver sección 13 del Plan Maestro sobre decisiones que requieren aprobación humana no aplica aquí directamente, pero sí amerita un ADR).
3. **Fuga de datos entre tenants en `RefreshToken`** — `RefreshToken` (Security 2.0) no implementa `ITenantEntity`: el filtro global de tenant no se aplica a esa tabla. Este módulo mitiga el riesgo acotando toda operación de sesiones a un `userId` concreto ya resuelto vía `UserManager` (que sí está tenant-filtrado) — ver el remarks de `IUserSessionStore` — pero un futuro método que liste sesiones sin ese acotamiento sí tendría el problema. No se corrigió `RefreshToken` en este corte (cambiaría el esquema de Security 2.0, fuera del alcance declarado de esta tarea).
4. **Consumidor no-HTTP** — `IIdentityAdministrationActorContext` solo tiene implementación HTTP (`HttpContextIdentityAdministrationActorContext`). Un futuro job/seeder que necesite invocar comandos de este módulo sin `HttpContext` necesitaría una variante `System`/`Null`, análoga a `NullTenantProvider`.
5. **Migraciones EF Core reales** — igual que el resto del repositorio (`Sample.Api`, `SecurityEndToEndTests`), este módulo usa `Database.EnsureCreatedAsync()` para materializar el esquema, no `dotnet ef migrations`; no hay ningún `DbContext` en todo el framework que use migraciones reales todavía. `IdentityAdministrationDbContext` fue verificado con `EnsureCreatedAsync()` contra SQL Server real (Testcontainers) en `samples/Sample.IdentityAdmin.Api.Tests`.
6. **Catálogo de roles global, no por-tenant** — `ApplicationRole` (Security 2.0) no implementa `ITenantEntity`, a diferencia de `ApplicationUser` que sí lo hace: es una decisión ya tomada en Fase 2, no introducida por este módulo. Pero este módulo es el primero que expone ese catálogo global vía una API HTTP con permisos por-tenant (`identidad.roles.ver`/`.crear`/`.permisos.administrar`), así que un actor con esos permisos ve y puede mutar los roles (nombres y permisos concedidos) de **todos** los tenants, no solo el propio. Mitigación aplicada en este corte: `IdentityAdministrationPermissions` documenta explícitamente (XML doc) que estos tres permisos son de alcance global y NUNCA deben otorgarse a un rol de administración de tenant regular, solo a un actor de plataforma/superadministrador. No se corrigió el esquema (agregar `TenantId` a `ApplicationRole` en Security 2.0 requiere un ADR y está fuera del alcance declarado de este módulo).
7. **N+1 en `ListarUsuariosQuery`** — hasta dos roundtrips extra por fila (`GetRolesAsync`/`IsLockedOutAsync` vía `UserManager`), hasta `2*pageSize` por página (pageSize máx. 100). Aceptado en este primer corte por simplicidad sobre `UserManager`; una optimización futura consultaría `AspNetUserRoles`/`AspNetUserClaims` en bloque para todo el `users.Select(u => u.Id)` de la página, en una sola consulta.

## Pruebas

- `samples/Sample.IdentityAdmin.Api.Tests/Integration/IdentityAdministrationEndpointsIntegrationTests.cs` — 8 pruebas de extremo a extremo contra SQL Server real (Testcontainers), vía `WebApplicationFactory<Program>`: creación de usuario + idempotencia, 401 sin autenticación, 403 sin permiso, asignación de rol (RBAC), auto-asignación denegada (ABAC), desactivación de usuario, listado/revocación de sesión, concesión/revocación de permiso a rol.
