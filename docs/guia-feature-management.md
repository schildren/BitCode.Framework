# Feature Management — Fase 6, módulo 4

**Tarea:** Fase 6 — Plataforma funcional empresarial, módulo 4 (Feature Management) del
[Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Dependencias declaradas:** Core (Fase 1) y Security (Fase 2) — ambas cerradas antes de esta tarea.
**Fecha:** 2026-09-09.

Módulo de plataforma que provee la capa de ADMINISTRACIÓN de feature flags: alta/consulta de flags con
persistencia propia, segmentación de audiencia (por tenant o por porcentaje), rollout (asociación de un
flag con uno o más segmentos) y un motor de evaluación real que responde "¿está este flag activo PARA
ESTE CONTEXTO concreto?". Mismo criterio que Organization/Catalogs and Parameters (Fase 6, módulos 2 y
3): `FeatureFlag`/`Segmento`/`Rollout` son `AggregateRoot<Guid>` **propios** de este módulo, no reutilizan
tipos ajenos de otro bounded context.

## Relación con F4-12 (léase antes de usar este módulo)

Este NO es el primer mecanismo de feature flags del repositorio. Fase 4 (F4-12, commits `23bccb5` y
`c7147fb`, documentado en `docs/politica-configuracion-y-feature-flags.md`) ya construyó
`IFeatureFlagProvider` (`Shared.Infrastructure.Security.FeatureFlags`): un flag on/off simple, leído
desde `IConfiguration` (`ConfigMap`/`appsettings`), con hot-reload real (`AddJsonFile(reloadOnChange:
true)`) y auditoría de actor de sistema fijo. Ese documento ya reservaba explícitamente, en su sección
2.2 ("Qué NO es -- frontera explícita con Fase 6"), las cuatro capacidades que este módulo entrega:
segmentación, rollout gradual, endpoint administrativo con actor autenticado y persistencia propia.

**Decisión explícita de esta tarea: los dos mecanismos CONVIVEN, ninguno reemplaza al otro.**

| Capacidad | F4-12 (`IFeatureFlagProvider`) | Feature Management (este módulo) |
|---|---|---|
| Origen del valor | `IConfiguration` (ConfigMap/appsettings) | Tabla SQL propia (`FeatureManagementDbContext`) |
| Evaluación on/off simple | Sí | Sí (flag sin rollouts asociados, ver "Algoritmo de evaluación") |
| Segmentación por tenant/porcentaje | No | Sí (`Segmento`) |
| Rollout gradual | No | Sí (`Rollout` + `EvaluarFeatureFlagQuery`) |
| Endpoint administrativo con actor autenticado | No | Sí (RBAC + ABAC, ver más abajo) |
| Auditoría de actor | Sistema fijo (`system.featureflags.config-reload`) | Actor humano identificado (JWT) |
| Latencia de propagación de un cambio | Ciclo de sync del kubelet (hasta ~1 min) si es `ConfigMap` en K8s | Inmediata (lectura síncrona contra SQL Server en cada evaluación, sin cache) |
| Caso de uso típico | Interruptor de infraestructura/runtime (por ejemplo, "activar el nuevo endpoint de health checks") gestionado por el pipeline de despliegue | Flag de negocio con targeting granular (por ejemplo, "nuevo checkout al 10% de usuarios de un tenant piloto") gestionado por un operador vía API |

Un consumidor real que solo necesita un interruptor global gestionado por despliegue (sin targeting, sin
UI administrativa) debe seguir usando `IFeatureFlagProvider` de F4-12 -- adoptar este módulo para ese
caso sería sobre-ingeniería (Plan Maestro, sección 3.2). Un consumidor que necesita targeting por
tenant/porcentaje o un flujo administrativo con auditoría de actor humano usa este módulo. **Ambos
mecanismos pueden convivir en el mismo host** (por ejemplo, `IFeatureFlagProvider` para flags de
infraestructura y `FeatureManagementDbContext` para flags de negocio) sin conflicto, porque no comparten
ni almacenamiento ni superficie de API.

## Ubicación

- Librería: `src/Platform/BitCode.Platform.FeatureManagement/` (namespace
  `BitCode.Framework.Platform.FeatureManagement`, ver `docs/convenciones.md`, sección "Namespaces de
  módulos de Platform").
- Aplicación de referencia: `samples/Sample.FeatureManagement.Api/`.
- Pruebas de integración (SQL Server real vía Testcontainers): `samples/Sample.FeatureManagement.Api.Tests/`.

## Capacidades

| Entidad | Alcance de este primer corte |
|---|---|
| FeatureFlag | Alta (nace apagado, nombre único por tenant) + obtención + listado paginado + activar/desactivar (RBAC + ABAC, emite `FeatureFlagActivadoIntegrationEvent`/`FeatureFlagDesactivadoIntegrationEvent`). |
| Segmento | Alta de dos tipos de criterio: `PorTenant` (coincidencia exacta de `TenantId`) y `PorPorcentaje` (bucketing determinístico 0-100) + obtención + listado paginado. |
| Rollout | Asocia un flag existente con un segmento existente (rechaza asociación duplicada, `Result.Failure` → 409) + listado por flag. Emite `RolloutIniciadoIntegrationEvent`. |
| Evaluación | `EvaluarFeatureFlagQuery`: dado un nombre de flag + contexto (`tenantId`/`userId` opcional), decide si está activo, con el motivo de la decisión (`Motivo`) para depuración. |

El Plan Maestro pide, si el tiempo no alcanza para los 2-3 tipos de criterio de segmento con la misma
calidad, priorizar 1-2 tipos bien hechos y dejar el resto documentado como pendiente. Este corte entrega
**dos** tipos (`PorTenant`, `PorPorcentaje`) con vertical slice completo y evidencia real de ejecución
(ver "Pruebas") -- un tercer tipo (por ejemplo, "por claim arbitrario") queda como pendiente explícito
(ver "Pendientes").

## Modelo de datos

```
FeatureFlag (AggregateRoot<Guid>, único por (TenantId, Nombre))
  Nombre, Descripcion, Activo (nace en false)

Segmento (AggregateRoot<Guid>, único por (TenantId, Nombre))
  Nombre, Tipo (PorTenant | PorPorcentaje)
  TenantIdCriterio (solo si Tipo = PorTenant)
  Porcentaje (solo si Tipo = PorPorcentaje, 0-100)

Rollout (AggregateRoot<Guid>, único por (FeatureFlagId, SegmentoId))
  FeatureFlagId, SegmentoId
```

Un `FeatureFlag` puede tener cero o más `Rollout`s (hacia distintos `Segmento`s) -- semántica OR entre
segmentos del mismo flag (pertenece a CUALQUIERA de ellos).

## Algoritmo de evaluación (`EvaluarFeatureFlagQuery`)

Corto-circuito, en este orden:

1. El flag no existe → `Result.Failure` (404).
2. `FeatureFlag.Activo == false` → inactivo para TODO contexto, sin importar segmentos/rollouts (breaker
   global -- útil para apagar de emergencia una capacidad sin tener que borrar sus rollouts).
3. El flag no tiene ningún `Rollout` asociado → activo para TODO contexto (flag on/off simple, sin
   targeting -- equivalente al caso más común de `IFeatureFlagProvider` de F4-12, pero resuelto desde
   persistencia propia).
4. El flag tiene rollouts → activo si el contexto pertenece a AL MENOS UNO de los segmentos asociados:
   - `PorTenant`: `request.TenantId == segmento.TenantIdCriterio`.
   - `PorPorcentaje`: bucket determinístico (SHA-256 de `{featureFlagId}:{userId ?? tenantId}`, módulo
     100) menor que `segmento.Porcentaje` -- el MISMO contexto siempre cae en el MISMO bucket para el
     MISMO flag (no es aleatorio en cada evaluación), ver `PorcentajeRolloutHasher`.
5. Ninguno de los rollouts aplica → inactivo para este contexto, a pesar de que el flag está globalmente
   activo (solo lo ven los segmentos elegidos).

La respuesta (`FeatureFlagEvaluationResponse`) incluye `Motivo` (por ejemplo,
`"flag-inactivo-globalmente"`, `"activo-sin-segmentacion"`, `"segmento:{nombre}"`,
`"fuera-de-todos-los-segmentos"`) para depurar por qué un contexto concreto ve o no una funcionalidad sin
inspeccionar los datos crudos.

## Cómo consumirlo desde un host

```csharp
// InfrastructureModule.cs
services.AddHttpContextTenantProvider();
services.AddSharedPersistence<FeatureManagementDbContext>(connectionString);
services.AddSharedSecurity<ApplicationUser, ApplicationRole, TuIdentityDbContext>(configuration);
services.AddSharedAbacAuthorization(options =>
{
    // Opcional: restringe qué flags puede activar/desactivar un actor cuyo rol solo administra un
    // subconjunto de flags.
    options.ScopeRules.Add(new AbacScopeAttributeRule
    {
        ResourceType = "featuremanagement.flags",
        ResourceAttributeKey = "featureFlagId",
        ClaimType = "feature_flag_id",
    });
});
services.AddSharedAuditing();
services.AddSharedFeatureManagement();
services.AddHttpContextIdempotencyKeyProvider();
services.AddSharedApplication(typeof(InfrastructureModule).Assembly, typeof(FeatureManagementDbContext).Assembly);

// TuApiModule.cs
[DependsOn(typeof(InfrastructureModule))]
public class TuApiModule : IWebFrameworkModule
{
    public void ConfigureApplication(WebApplication app) => app.MapFeatureManagementEndpoints();
}
```

Ver `samples/Sample.FeatureManagement.Api/` para el host de referencia completo.

## Endpoints

### Flags (`/api/v1/feature-flags/...`)

| Método | Ruta | Permiso | Notas |
|---|---|---|---|
| POST | `/` | `featuremanagement.flags.crear` | Idempotente (F1-22). Nace apagado. 409 si el nombre ya existe. |
| GET | `/{id}` | `featuremanagement.flags.ver` | |
| GET | `/` | `featuremanagement.flags.ver` | Paginado. |
| POST | `/{id}/activar` | `featuremanagement.flags.activar` | RBAC + ABAC (alcance por `featureFlagId`). 409 si ya está activo. |
| POST | `/{id}/desactivar` | `featuremanagement.flags.desactivar` | RBAC + ABAC (alcance por `featureFlagId`). 409 si ya está inactivo. |
| GET | `/{nombre}/evaluar?tenantId=&userId=` | `featuremanagement.flags.evaluar` | Permiso separado del CRUD (ver más abajo), la consulta de evaluación real. |

### Segmentos (`/api/v1/segmentos/...`)

| Método | Ruta | Permiso | Notas |
|---|---|---|---|
| POST | `/` | `featuremanagement.segmentos.crear` | `Tipo` distingue `PorTenant`/`PorPorcentaje`; el resto de los campos se valida condicionalmente. |
| GET | `/{id}` | `featuremanagement.segmentos.ver` | |
| GET | `/` | `featuremanagement.segmentos.ver` | Paginado. |

### Rollouts (`/api/v1/rollouts/...`)

| Método | Ruta | Permiso | Notas |
|---|---|---|---|
| POST | `/` | `featuremanagement.rollouts.crear` | 404 si el flag o el segmento no existen; 409 si la asociación ya existe. |
| GET | `/por-flag/{featureFlagId}` | `featuremanagement.rollouts.ver` | |

## RBAC + ABAC en activar/desactivar un flag (requisito común de Fase 6)

`FeatureManagementPermissions.FlagsActivar`/`FlagsDesactivar` son permisos DISTINTOS y más restrictivos
que `FlagsCrear` (crear un flag apagado no tiene efecto observable en producción; encenderlo/apagarlo sí)
-- mismo criterio que `CatalogsPermissions.VersionesPublicar` (Catalogs and Parameters) y
`OrganizationPermissions.EmpresasDesactivar` (Organization). Además del permiso RBAC, ambos handlers
evalúan explícitamente `IAuthorizationPolicyEvaluator` (F2-08) con la regla ABAC incorporada del
framework (`AttributeScopeAbacRule`) sobre el atributo `featureFlagId` -- un consumidor real restringe
qué flags puede activar un actor cuyo rol solo administra un subconjunto, configurando
`AbacOptions.ScopeRules` con `ResourceType = "featuremanagement.flags"`,
`ResourceAttributeKey = "featureFlagId"` y el `ClaimType` que transporte el alcance del actor. El host de
referencia (`Sample.FeatureManagement.Api`) NO registra esta regla por defecto (lista vacía = sin
restricción adicional más allá de RBAC) -- el test de integración la configura explícitamente para
demostrar el criterio de aceptación de punta a punta (ver "Pruebas").

`FeatureManagementPermissions.FlagsEvaluar` es un permiso SEPARADO de `FlagsVer`: evaluar si un flag está
activo para un contexto es la operación que consumen aplicaciones cliente en runtime, con un volumen de
llamadas muy superior a la administración -- separarlo permite otorgar este permiso a un actor de
servicio (aplicación cliente) sin darle acceso al CRUD administrativo.

## Auditoría

Toda mutación (`crear`/`activar`/`desactivar` de flags, `crear` de segmentos y rollouts) escribe una
`AuditEntry` vía `IAuditWriter` (F2-15), con el actor resuelto de `IFeatureManagementActorContext`
(usuario autenticado del JWT, o `"system"` fuera de un pipeline HTTP). Las decisiones ABAC denegadas
(`AuditOutcome.Denied`) también quedan auditadas -- mismo criterio que
`PublicarCatalogoVersionCommandHandler` (Catalogs and Parameters).

## Eventos de dominio e integración

`FeatureFlag.Activar()`/`Desactivar()` y el constructor de `Rollout` levantan
`FeatureFlagActivadoIntegrationEvent`/`FeatureFlagDesactivadoIntegrationEvent`/
`RolloutIniciadoIntegrationEvent` vía `RaiseDomainEvent` -- `OutboxSaveChangesInterceptor` (F1-23) los
persiste atómicamente junto con el cambio de negocio en `FeatureManagementDbContext`. Registrados en
`docs/catalogo-eventos.md` (regla dura 27). `samples/Sample.FeatureManagement.Api` no registra
`AddSharedKafkaEventing` -- los eventos quedan en `OutboxMessage` sin relay activo (mismo estado que
Organization/Catalogs and Parameters; el mecanismo de publicación en sí ya está probado de punta a punta
en `Sample.Eventing`, F3-13).

## Pendientes explícitos

1. **Solo 2 de los 3 tipos de criterio de segmento sugeridos por el Plan Maestro.** `PorTenant` y
   `PorPorcentaje` están completos con vertical slice y pruebas reales; un tercer tipo (por ejemplo, "por
   claim arbitrario del JWT", análogo a `AbacScopeAttributeRule`) queda como extensión natural del mismo
   patrón (`SegmentoTipo` + un caso nuevo en `EvaluarFeatureFlagQueryHandler.Pertenece`), no implementado
   en este corte por decisión explícita de alcance (priorizar 1-2 tipos bien hechos, Plan Maestro).
2. **Condición de carrera real en la validación de "nombre de flag duplicado".** Mismo hallazgo que
   `CrearCatalogoCommandHandler` (Catalogs and Parameters, `docs/guia-catalogs.md`, pendiente 4):
   `CrearFeatureFlagCommandHandler` valida con `AnyAsync` (check) y recién después inserta (act), sin
   `ITransactionalCommand` ni aislamiento serializable. A diferencia de `ParametroVigencia`, acá SÍ existe
   un guardrail de datos (índice único `(TenantId, Nombre)` en `FeatureManagementDbContext`) que evita que
   dos flags duplicados terminen persistidos bajo una carrera real -- pero la carrera perdedora recibe una
   `DbUpdateException` no traducida a `Result.Failure`/409 en este primer corte (el
   `ExceptionHandlerMiddleware` genérico la convierte en un 500 en vez de un 409 amigable). Mismo
   razonamiento y mismo pendiente para `CrearRolloutCommandHandler` con el índice único
   `(FeatureFlagId, SegmentoId)`.
3. **Sin "cerrar" un rollout (eliminar la asociación flag-segmento) tras crearlo.** No hay un
   `EliminarRolloutCommand` en este primer corte -- un consumidor real que necesite retirar un segmento
   de un rollout debe extenderlo (mismo patrón que `Remove` de `IRepository<T, TId>`, con su propio
   evento de integración simétrico a `RolloutIniciadoIntegrationEvent`).
4. **Evaluación sin cache.** `EvaluarFeatureFlagQuery` consulta SQL Server en cada llamada (3 queries:
   flag, rollouts, segmentos) -- para el volumen de llamadas típico de un flag consumido por una
   aplicación cliente en cada request, un consumidor real de alto tráfico debería cachear el resultado
   (`Shared.Infrastructure.Caching`, con invalidación al recibir
   `FeatureFlagActivadoIntegrationEvent`/`FeatureFlagDesactivadoIntegrationEvent`/`RolloutIniciadoIntegrationEvent`)
   -- no implementado en este corte porque la regla dura del Plan Maestro prohíbe usar cache como fuente
   de verdad sin haber decidido explícitamente la estrategia de invalidación, y este primer corte prioriza
   la corrección del motor de evaluación sobre su rendimiento a escala.
5. **Sin `dotnet ef migrations` real.** Mismo estado que el resto del repositorio (`EnsureCreatedAsync` en
   `Program.cs` de `Sample.FeatureManagement.Api`) -- no es deuda nueva de este módulo.
6. **Sin publicación real contra un broker Kafka productivo.** Ver sección "Eventos de dominio e
   integración" arriba.
7. **Host de referencia con dos bases de datos separadas.** Mismo patrón que
   `Sample.Catalogs.Api`/`Sample.Organization.Api` -- `FeatureManagementDbContext`
   (`ConnectionStrings:Default`) y un `SampleIdentityDbContext` propio (`ConnectionStrings:Identity`) solo
   para poder emitir JWTs reales en las pruebas de integración.

## Pruebas

`samples/Sample.FeatureManagement.Api.Tests/Integration/FeatureManagementEndpointsIntegrationTests.cs` --
contra SQL Server real (Testcontainers, `SqlServerContainerFixture`), vía `WebApplicationFactory<Program>`:

- Alta de flag sin autenticación → 401.
- Alta de flag con permiso → 201 + idempotencia (misma `Idempotency-Key` + mismo cuerpo → mismo Id).
- Alta de flag con nombre duplicado → 409.
- Activar/desactivar un flag cambia su estado; activar uno ya activo → 409 (`Result.Failure`, nunca una
  excepción).
- Activar un flag fuera del alcance ABAC configurado (`feature_flag_id`) → 403, aunque el actor tenga el
  permiso RBAC `featuremanagement.flags.activar`.
- Evaluación de un flag inactivo → siempre `false` (`Motivo = "flag-inactivo-globalmente"`), sin importar
  contexto.
- Evaluación de un flag activo sin ningún rollout asociado → siempre `true`
  (`Motivo = "activo-sin-segmentacion"`) para cualquier tenant.
- Vertical slice completo de segmentación por tenant: flag activo + rollout hacia un segmento `PorTenant`
  → activo solo para el tenant configurado, inactivo para cualquier otro.
- Vertical slice completo de rollout por porcentaje: segmento al 0% nunca incluye a nadie, segmento al
  100% siempre incluye a todos (casos deterministas de los extremos del algoritmo de bucketing).
- Alta de rollout con la misma asociación flag-segmento dos veces → 409.

Comando de verificación real (evidencia de ejecución en el reporte de cierre de esta tarea):

```powershell
dotnet test samples/Sample.FeatureManagement.Api.Tests/Sample.FeatureManagement.Api.Tests.csproj
```

Resultado real de la última ejecución: 10 pruebas, 10 correctas, 0 fallidas (contra SQL Server real vía
Testcontainers).
