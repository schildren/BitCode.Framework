# Fase 3 — Seguridad y autorización

**Estado:** Completa
**Commits:** `c342f17` … `5c0f99c` (6 commits)
**Tests:** 18 en `Shared.Infrastructure.Security.Tests` (16 unitarios/con Identity real sobre SQLite + 2 de integración contra SQL Server real)

## Objetivo

Cubrir el hueco que la auditoría inicial de BC-SFE-MID marcó como **INEXISTENTE**: autenticación, sistema de permisos y autorización basada en políticas. BC-SFE-MID solo tenía una autenticación por API Key ad-hoc para su caso específico (B2B), sin nada reutilizable como framework genérico.

## Decisiones de diseño

| Decisión | Elegido | Motivo |
|---|---|---|
| Autenticación | JWT Bearer simple (no OpenIddict) | Cubre el caso típico de una API consumida por un frontend propio; un servidor OAuth2/OIDC completo (OpenIddict) solo se justifica si el framework debe actuar como Identity Provider para terceros |
| Almacenamiento de Identity | Mismo `MultiTenantDbContext` del proyecto consumidor | Mismas migraciones, misma conexión, filtros multi-tenant de la Fase 1 aplican igual a Users/Roles |

## Problema de diseño resuelto: Identity + multi-tenancy en el mismo `DbContext`

`MultiTenantDbContext` (Fase 1) no puede heredar simultáneamente de `IdentityDbContext` — C# no permite herencia múltiple. La solución fue extraer la lógica de filtro global y caché de modelo (antes un método de instancia de `MultiTenantDbContext`) a un helper estático, **`MultiTenancyModelConfigurator`**, reutilizable tanto por `MultiTenantDbContext` como por la nueva base `MultiTenantIdentityDbContext<TUser,TRole> : IdentityDbContext<TUser,TRole,Guid>`. Un proyecto sin necesidad de autenticación sigue usando `MultiTenantDbContext` sin ninguna dependencia a Identity; uno que sí la necesita hereda de `MultiTenantIdentityDbContext` y obtiene ambos comportamientos sin duplicar código.

## Componentes por tarea

### Tarea 3.1 — Entidades de Identity y `MultiTenantIdentityDbContext`

- `ApplicationUser : IdentityUser<Guid>` implementando también `IAuditedEntity`/`ISoftDelete`/`ITenantEntity` de la Fase 1 — un usuario se audita, se soft-elimina y se aísla por tenant igual que cualquier otra entidad del framework.
- `ApplicationRole : IdentityRole<Guid>`.
- `MultiTenantIdentityDbContext<TUser,TRole>`: aplica el mismo `PerInstanceModelCacheKeyFactory` y `MultiTenancyModelConfigurator.ApplyGlobalFilters` que `MultiTenantDbContext`, verificado con un test que confirma aislamiento de `ApplicationUser` por tenant (el mismo bug de caché de modelo de la Fase 1 podría haber reaparecido aquí; el test lo descarta empíricamente).

### Tarea 3.2 — JWT y refresh tokens

- `JwtOptions` (SecretKey/Issuer/Audience/expiraciones), `JwtTokenGenerator`: emite access tokens HMAC-SHA256 con claims de usuario, roles y claims adicionales (permisos), y refresh tokens vía `RandomNumberGenerator` (no `Guid.NewGuid()`, que no es criptográficamente seguro).
- `RefreshToken` expuesto como `DbSet<RefreshToken>` directamente en `MultiTenantIdentityDbContext` — todo consumidor lo tiene disponible sin declararlo.

### Tarea 3.3 — Sistema de permisos

Decisión clave: **los permisos no tienen tablas propias**. Se modelan como `Claim`s de tipo `"permission"` sobre los roles de Identity (tabla `AspNetRoleClaims`, ya provista), evitando duplicar un esquema `Permission`/`RolePermission` cuando Identity ya resuelve ese problema.

- `RoleManagerPermissionExtensions.AddPermissionAsync`: agrega un permiso a un rol de forma idempotente.
- `PermissionService<TUser,TRole>`: agrega los permisos de todos los roles de un usuario en un único conjunto sin duplicados.

### Tarea 3.4 — Policy-based Authorization dinámica

- `PermissionRequirement` + `PermissionAuthorizationHandler`: resuelve el `userId` del claim `NameIdentifier` y consulta `IPermissionService`.
- `PermissionAuthorizationPolicyProvider`: sintetiza una `AuthorizationPolicy` por cada nombre de permiso **bajo demanda**, sin pre-registrar una policy por permiso con `AddPolicy`. `[RequirePermission("productos.crear")]` equivale a `[Authorize(Policy = "productos.crear")]`.

**Bug real detectado durante el desarrollo:** la primera versión de `PermissionAuthorizationPolicyProvider` sintetizaba una `PermissionRequirement` para **cualquier** nombre de policy, incluidas las registradas explícitamente con `AddPolicy` — rompiendo cualquier política "normal" que el proyecto consumidor ya tuviera. Se corrigió delegando primero al `DefaultAuthorizationPolicyProvider` y solo sintetizando cuando el nombre no coincide con ninguna policy conocida; un test verifica que una policy explícita tiene prioridad y no se reemplaza silenciosamente.

### Tarea 3.5 — `AddSharedSecurity<TUser,TRole,TContext>()`

Registra en un solo paso: Identity (`UserManager`/`RoleManager` sobre `TContext`), autenticación JWT Bearer con `TokenValidationParameters` construidos desde `JwtOptions` (`ClockSkew = TimeSpan.Zero` para no tolerar tokens vencidos), y el sistema de permisos + autorización dinámica ya conectados. Lanza una excepción explícita y temprana si falta la sección de configuración `"Jwt"`, en vez de fallar más adelante con un error opaco al construir las credenciales de firma.

### Tarea 3.6 — Test de extremo a extremo contra SQL Server real

Con `Testcontainers.MsSql` (mismo patrón que la Fase 1): crea usuario y rol con Identity real, otorga un permiso, emite un JWT, resuelve los permisos desde la base real, y confirma que `PermissionAuthorizationHandler` aprueba con el permiso correcto y deniega sin él — sin mocks, replicando exactamente el camino que seguiría un consumidor real.

## Cómo usarlo desde un proyecto consumidor

```csharp
// 1. DbContext con Identity + multi-tenancy
public class MyAppDbContext(DbContextOptions<MyAppDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantIdentityDbContext<ApplicationUser, ApplicationRole>(options, tenantProvider)
{
    public DbSet<Producto> Productos => Set<Producto>();
}

// 2. Registro (appsettings.json necesita la sección "Jwt")
services.AddSharedSecurity<ApplicationUser, ApplicationRole, MyAppDbContext>(configuration);

// 3. Seed de un rol con permisos
var role = new ApplicationRole { Name = "Editor" };
await roleManager.CreateAsync(role);
await roleManager.AddPermissionAsync(role, "productos.crear");

// 4. Proteger un endpoint
app.MapPost("/productos", ...).RequireAuthorization("productos.crear");
// o en un controller: [RequirePermission("productos.crear")]

// 5. Login: emitir el JWT
var user = await userManager.FindByNameAsync(request.UserName);
var roles = await userManager.GetRolesAsync(user);
var accessToken = tokenGenerator.GenerateAccessToken(user, roles, []);
var refreshToken = tokenGenerator.GenerateRefreshToken();
```

## Cobertura de tests

| Área | Tests |
|---|---|
| `MultiTenantIdentityDbContext` (modelo + aislamiento por tenant) | 2 |
| `JwtTokenGenerator` | 3 |
| `PermissionService` (con `UserManager`/`RoleManager` reales) | 3 |
| `PermissionAuthorizationHandler` | 3 |
| `PermissionAuthorizationPolicyProvider` | 2 |
| `AddSharedSecurity` (registro DI) | 3 |
| Extremo a extremo contra SQL Server real | 2 |
| **Total Fase 3** | **18** |

## Pendiente / fuera de alcance de esta fase

- Endpoints de login/refresh/logout concretos (Minimal API) — quedan como responsabilidad del proyecto consumidor, el framework provee las piezas (`IJwtTokenGenerator`, `UserManager`, `RefreshToken`) pero no impone la forma del endpoint.
- Revocación/rotación de refresh tokens (invalidar el anterior al emitir uno nuevo) — la entidad `RefreshToken` soporta `RevokedAtUtc`, pero la lógica de rotación no se implementó como servicio; queda como extensión natural para cuando se construya el endpoint de refresh.
- OpenIddict como alternativa para escenarios de Identity Provider — explícitamente descartado en esta fase por decisión del usuario, no bloqueante para retomarlo después si surge un caso de uso real.
- Fase 4 del plan general (infraestructura transversal: exception handling con `ProblemDetails`, observabilidad, background jobs, caching) — siguiente fase a desarrollar.
