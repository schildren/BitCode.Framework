# Organization — Fase 6, módulo 2

**Tarea:** Fase 6 — Plataforma funcional empresarial, módulo 2 (Organization) del
[Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Dependencias declaradas:** Core (Fase 1) y Audit (Fase 2) — ambas cerradas antes de esta tarea.
**Fecha:** 2026-09-08.

Módulo de plataforma que administra empresas, sucursales, áreas y cargos como una jerarquía
organizacional de **4 niveles fijos** (Empresa → Sucursal → Área → Cargo), reutilizable por cualquier
host consumidor. A diferencia de Identity Administration (Fase 6, módulo 1), que reutilizaba tipos
ajenos de Security 2.0 sin poder levantar eventos de dominio reales, `Empresa` y `Sucursal` son
`AggregateRoot<TId>` **propios** de este módulo: sus operaciones de negocio levantan eventos de dominio
reales, capturados por el mecanismo de Outbox (F1-23) y registrados como los primeros eventos
productivos del repositorio en `docs/catalogo-eventos.md`.

## Ubicación

- Librería: `src/Platform/BitCode.Platform.Organization/` (namespace `BitCode.Framework.Platform.Organization`,
  ver `docs/convenciones.md`, sección "Namespaces de módulos de Platform").
- Aplicación de referencia: `samples/Sample.Organization.Api/`.
- Pruebas de integración (SQL Server real vía Testcontainers): `samples/Sample.Organization.Api.Tests/`.

## Capacidades

| Entidad | Alcance de este primer corte |
|---|---|
| Empresa | CRUD completo (alta, obtención, listado paginado) + desactivación con RBAC/ABAC combinados. Emite `EmpresaCreadaIntegrationEvent`/`EmpresaDesactivadaIntegrationEvent`. |
| Sucursal | CRUD completo bajo una empresa existente y activa + desactivación con RBAC simple. Emite `SucursalCreadaIntegrationEvent`. |
| Área | Alta y listado (sin paginación) bajo una sucursal existente. Auto-referencia opcional (`ParentAreaId`) para sub-áreas dentro de la misma sucursal — sin desactivación, sin eventos de dominio propios, sin consulta de árbol recursivo (ver "Pendientes"). |
| Cargo | Alta y listado (sin paginación) bajo un área existente — hoja de la jerarquía, sin sub-cargos. |

Esta asimetría de alcance (Empresa/Sucursal con corte completo, Área/Cargo simplificados) es una
decisión explícita del primer corte, no un olvido — ver "Pendientes" más abajo. El Plan Maestro exige
una jerarquía de 4 niveles fijos ("empresas, sucursales, áreas, cargos y jerarquías"), no un modelo de
grafo genérico de profundidad arbitraria: por eso `Area.ParentAreaId` es la única auto-referencia del
modelo (sub-áreas dentro de la MISMA sucursal), y no existe un mecanismo de jerarquía adicional entre
empresas, entre sucursales, ni entre cargos.

## Modelo de datos

`OrganizationDbContext` (hereda de `MultiTenantDbContext`, Shared.Infrastructure.Persistence) es el
dueño exclusivo de las tablas `Empresas`, `Sucursales`, `Areas`, `Cargos` (más `IdempotencyKeys`/
`OutboxMessages`/`InboxMessages`, configuradas automáticamente por la clase base). Ningún otro módulo
de plataforma debe leer/escribir estas tablas directamente.

- `Empresa` (`AggregateRoot<Guid>`, `ITenantEntity`, `IAuditedEntity`, `ISoftDelete`): `RazonSocial`,
  `Identificador` (CUIT/RUC/NIF, sin formato validado por país todavía), `Activa`.
- `Sucursal` (`AggregateRoot<Guid>`, `ITenantEntity`, `IAuditedEntity`, `ISoftDelete`): `EmpresaId`
  (FK sin navegación EF Core), `Nombre`, `Direccion`, `Activa`.
- `Area` (`Entity<Guid>`, `ITenantEntity`, `IAuditedEntity`, `ISoftDelete`): `SucursalId`, `Nombre`,
  `ParentAreaId` (nullable, auto-referencia dentro de la misma sucursal).
- `Cargo` (`Entity<Guid>`, `ITenantEntity`, `IAuditedEntity`, `ISoftDelete`): `AreaId`, `Nombre`.

Todas implementan `ITenantEntity` (Fase 1) e `IAuditedEntity`/`ISoftDelete` (Fase 2) -- el filtro global
de tenant/soft-delete de `MultiTenantDbContext` aplica automáticamente a las cuatro. Ningún `DbContext`
se expone como `IQueryable` fuera del repositorio (regla dura, `docs/convenciones.md`): toda lectura
pasa por `IReadRepository<TEntity,TId>`/`ISpecification<TEntity>` (F1-17/F1-18).

## Cómo consumirlo desde un host

```csharp
// InfrastructureModule.cs del host consumidor
services.AddHttpContextTenantProvider();               // F1-12, antes de AddSharedPersistence
services.AddSharedPersistence<OrganizationDbContext>(connectionString);

// El host necesita su PROPIA infraestructura de identidad/RBAC (Organization no es dueño de
// usuarios/roles) -- normalmente compuesta junto con Identity Administration (Fase 6, módulo 1) en un
// consumidor real. Ver Sample.Organization.Api para un DbContext de identidad mínimo de referencia.
services.AddSharedSecurity<ApplicationUser, ApplicationRole, TIdentityDbContext>(configuration);

// F2-08/F2-10: ABAC -- requerido por DesactivarEmpresaCommandHandler. Un consumidor real configura
// aquí su propia regla de alcance sobre "organizacion.empresas" (ver la sección de abajo).
services.AddSharedAbacAuthorization(options =>
{
    options.ScopeRules.Add(new AbacScopeAttributeRule
    {
        ResourceType = "organizacion.empresas",
        ResourceAttributeKey = "empresaId",
        ClaimType = "empresa_id",
    });
});

services.AddSharedAuditing();          // F2-15
services.AddSharedOrganization();      // actor de auditoría/ABAC + health check propio

services.AddHttpContextIdempotencyKeyProvider();   // F1-22, antes de AddSharedApplication
services.AddSharedApplication(
    typeof(InfrastructureModule).Assembly,
    typeof(OrganizationDbContext).Assembly);        // handlers/validators del módulo, ensamblado propio
services.AddSharedExceptionHandling();

services.AddSharedApiVersioning();
services.AddSharedOpenApiForApiVersion(1, options => options.AddJwtBearerSecurityScheme());
```

```csharp
// Módulo del host que activa los endpoints (mismo patrón que Identity Administration)
[DependsOn(typeof(InfrastructureModule))]
public class OrganizationApiModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

    public void ConfigureApplication(WebApplication app) => app.MapOrganizationEndpoints();
}
```

Ejemplo ejecutable completo: `samples/Sample.Organization.Api/` (`InfrastructureModule.cs`,
`OrganizationApiModule.cs`, `Program.cs`) y sus tests de integración en
`samples/Sample.Organization.Api.Tests/Integration/OrganizationEndpointsIntegrationTests.cs`.

## Endpoints (`/api/v1/organizacion/...`)

| Método y ruta | Permiso RBAC | ABAC adicional |
|---|---|---|
| `POST /empresas` | `organizacion.empresas.crear` | — |
| `GET /empresas/{id}` | `organizacion.empresas.ver` | — |
| `GET /empresas` (paginado) | `organizacion.empresas.ver` | — |
| `POST /empresas/{id}/desactivar` | `organizacion.empresas.desactivar` | `AttributeScopeAbacRule` sobre `empresaId` |
| `POST /empresas/{empresaId}/sucursales` | `organizacion.sucursales.crear` | — |
| `GET /empresas/{empresaId}/sucursales` (paginado) | `organizacion.sucursales.ver` | — |
| `GET /sucursales/{id}` | `organizacion.sucursales.ver` | — |
| `POST /sucursales/{id}/desactivar` | `organizacion.sucursales.desactivar` | — |
| `POST /sucursales/{sucursalId}/areas` | `organizacion.areas.crear` | — |
| `GET /sucursales/{sucursalId}/areas` | `organizacion.areas.ver` | — |
| `POST /areas/{areaId}/cargos` | `organizacion.cargos.crear` | — |
| `GET /areas/{areaId}/cargos` | `organizacion.cargos.ver` | — |

Todas las mutaciones (`POST`) exigen un header `Idempotency-Key` (F1-22, `IIdempotentCommand`): un
reintento con la misma clave y el mismo cuerpo devuelve el mismo resultado sin duplicar el alta.

## RBAC + ABAC en la desactivación de empresas (requisito común de Fase 6)

El caso de referencia del módulo para "RBAC y ABAC en operaciones sensibles": desactivar una empresa
completa exige `organizacion.empresas.desactivar` -- un permiso **distinto y más restrictivo** que
`organizacion.empresas.crear`/`organizacion.sucursales.crear` (requisito literal de la Fase 6). Además,
`DesactivarEmpresaCommandHandler` evalúa explícitamente `IAuthorizationPolicyEvaluator` (F2-08) con la
regla ABAC **incorporada** del framework, `AttributeScopeAbacRule` -- a diferencia de Identity
Administration (que escribió su propia regla, `SelfRoleAssignmentAbacRule`, para una restricción
ad-hoc de no-autoasignación), este caso encaja exactamente en el patrón genérico que el framework ya
trae para "empresa/sucursal" (ver el ejemplo de uso en `AbacOptions`, Shared.Infrastructure.Security):
un consumidor real limita qué empresas puede desactivar un actor cuyo rol solo administra un
subconjunto de empresas del mismo tenant (por ejemplo, un grupo corporativo con varias razones
sociales), configurando `AbacOptions.ScopeRules` con `ResourceType = "organizacion.empresas"`,
`ResourceAttributeKey = "empresaId"` y el `ClaimType` que transporte el alcance del actor (por ejemplo,
`"empresa_id"`, ver el JWT del actor). Sin ningún claim de ese tipo, la regla no restringe (política de
"sin dato, no se restringe", ver `AttributeScopeAbacRule`) -- así que un actor de plataforma sin ese
claim (superadministrador) puede desactivar cualquier empresa, mientras que un actor con el claim
limitado solo puede desactivar las empresas que ese claim habilita, aunque tenga el permiso RBAC.

`Sample.Organization.Api` (el host de referencia) **no** registra ninguna regla de alcance por
defecto (`AbacOptions.ScopeRules` vacío) -- es responsabilidad de cada consumidor real decidir su
propio criterio de alcance. El test
`DesactivarEmpresa_FueraDeAlcanceAbac_EsDenegadaAunqueTengaElPermisoRbac`
(`samples/Sample.Organization.Api.Tests/Integration/OrganizationEndpointsIntegrationTests.cs`) configura
esa regla explícitamente vía `WithWebHostBuilder`, solo para demostrar el mecanismo de punta a punta.

`DesactivarSucursalCommand`, en cambio, solo exige `organizacion.sucursales.desactivar` (RBAC simple,
sin capa ABAC adicional) -- desactivar una sucursal individual es un alcance más acotado que desactivar
la empresa completa (con todas sus sucursales).

## Jerarquía organizacional

Empresa → Sucursal → Área → Cargo es una jerarquía de **4 niveles fijos**, no un árbol genérico de
profundidad arbitraria: cada nivel referencia únicamente al nivel inmediato superior por FK (`EmpresaId`
en `Sucursal`, `SucursalId` en `Area`, `AreaId` en `Cargo`). La única auto-referencia del modelo es
`Area.ParentAreaId` (sub-áreas dentro de la MISMA sucursal), validada en `CrearAreaCommand` (el área
padre debe existir y pertenecer a la misma sucursal) -- sin validación de ciclos todavía (pendiente
explícito). `Cargo` es la hoja de la jerarquía: no admite "sub-cargos".

## Auditoría

Toda mutación (`CrearEmpresaCommand`, `DesactivarEmpresaCommand`, `CrearSucursalCommand`,
`DesactivarSucursalCommand`) escribe una entrada vía `IAuditWriter` (F2-15) con actor, tenant, acción,
recurso y resultado (éxito/denegado/error) -- mismo patrón que Identity Administration. `Area`/`Cargo`
(corte simplificado) no auditan sus altas todavía, ver "Pendientes".

## Pendientes explícitos

Honestidad sobre el alcance de este primer corte (mismo criterio que Identity Administration documentó
sus propios pendientes, por ejemplo, delegaciones):

1. **Área y Cargo sin desactivación ni eventos de dominio propios.** Solo tienen alta y listado -- no
   hay `DesactivarAreaCommand`/`DesactivarCargoCommand`, ni `AreaCreadaIntegrationEvent`/
   `CargoCreadoIntegrationEvent`. Si un consumidor real necesita ese alcance completo, es una extensión
   natural del mismo patrón ya usado en `Empresa`/`Sucursal` (convertirlas en `AggregateRoot<Guid>`).
2. **Sin consulta de árbol recursivo para `Area`.** `ListarAreasQuery` devuelve la lista plana de una
   sucursal (con `ParentAreaId` para que el cliente arme el árbol) -- no hay un endpoint que devuelva la
   jerarquía completa anidada ni una recursión SQL (`WITH RECURSIVE`/CTE recursivo).
3. **Sin validación de ciclos en `Area.ParentAreaId`.** `CrearAreaCommand` valida que el área padre
   exista y pertenezca a la misma sucursal, pero no impide una cadena que termine formando un ciclo
   (poco probable en la práctica dado que un área nueva no puede referenciar un área que todavía no
   existe, pero no hay una prueba explícita de esa invariante).
4. **Sin auditoría en el alta de Área/Cargo.** Consistente con su alcance simplificado (punto 1).
5. **Sin `dotnet ef migrations` real.** Mismo estado que el resto del repositorio (`EnsureCreatedAsync`
   en `Program.cs` de `Sample.Organization.Api`, sin migraciones versionadas) -- no es deuda nueva de
   este módulo.
6. **Sin publicación real contra un broker Kafka productivo.** Los tres eventos productivos
   (`Organizacion.EmpresaCreada`/`EmpresaDesactivada`/`SucursalCreada`) quedan escritos en
   `OutboxMessage` -- ningún host de referencia de este repositorio activa `AddSharedKafkaEventing`
   todavía. El mecanismo de relay/publicación en sí ya está probado de punta a punta por
   `samples/Sample.Eventing` (F3-13); activarlo para Organization es una tarea de integración del host
   consumidor real, no un gap del mecanismo.
7. **Sin formato de `Identificador` validado por país.** `Empresa.Identificador` acepta cualquier
   string no vacío de hasta 32 caracteres -- sin validación de dígito verificador de CUIT/RUC/NIF.
8. **Host de referencia con dos bases de datos separadas.** `Sample.Organization.Api` usa
   `OrganizationDbContext` (`ConnectionStrings:Default`) y un `SampleIdentityDbContext` propio
   (`ConnectionStrings:Identity`) solo para poder emitir JWTs reales en los tests -- un consumidor real
   probablemente compone Organization junto con Identity Administration (Fase 6, módulo 1) en su propio
   host, con su propia estrategia de cuántas bases de datos usar.

## Pruebas

`samples/Sample.Organization.Api.Tests/Integration/OrganizationEndpointsIntegrationTests.cs` -- contra
SQL Server real (Testcontainers, `SqlServerContainerFixture`), vía `WebApplicationFactory<Program>`:

- Alta de empresa sin autenticación → 401.
- Alta de empresa con permiso → 201 + idempotencia (misma `Idempotency-Key` + mismo cuerpo → mismo Id).
- Lectura de empresa sin permiso → 403.
- Alta de sucursal bajo empresa activa → 201, aparece en el listado paginado.
- Alta de sucursal bajo empresa desactivada → 400 (`Organizacion.Sucursales.EmpresaInactiva`).
- Desactivación de empresa fuera del alcance ABAC configurado (`empresa_id`) → 403, aunque el actor
  tenga el permiso RBAC `organizacion.empresas.desactivar`.
- Desactivación de sucursal con permiso simple (sin ABAC adicional) → 200, refleja `Activa = false`.
- Alta de área y cargo bajo la jerarquía completa (empresa → sucursal → área → cargo) → 201 en cada
  paso, ambos aparecen en sus listados respectivos.

Comando de verificación real (evidencia de ejecución en el reporte de cierre de esta tarea):

```powershell
dotnet test samples/Sample.Organization.Api.Tests/Sample.Organization.Api.Tests.csproj
```
