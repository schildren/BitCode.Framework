# Catalogs and Parameters — Fase 6, módulo 3

**Tarea:** Fase 6 — Plataforma funcional empresarial, módulo 3 (Catalogs and Parameters) del
[Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Dependencias declaradas:** Core (Fase 1) — cerrada antes de esta tarea.
**Fecha:** 2026-09-09.

Módulo de plataforma que provee dos capacidades reutilizables por cualquier host consumidor: catálogos
versionados (listas de valores que evolucionan en el tiempo sin romper referencias externas ya
resueltas) y parámetros con vigencia (valores de configuración que cambian en el tiempo, con la
consulta explícita "¿cuál es el valor vigente hoy o en una fecha dada?"). Mismo criterio que
Organization (Fase 6, módulo 2): `Catalogo`/`CatalogoVersion`/`Parametro`/`ParametroVigencia` son
`AggregateRoot<Guid>` **propios** de este módulo, no reutilizan tipos ajenos de otro bounded context.

## Ubicación

- Librería: `src/Platform/BitCode.Platform.Catalogs/` (namespace `BitCode.Framework.Platform.Catalogs`,
  ver `docs/convenciones.md`, sección "Namespaces de módulos de Platform").
- Aplicación de referencia: `samples/Sample.Catalogs.Api/`.
- Pruebas de integración (SQL Server real vía Testcontainers): `samples/Sample.Catalogs.Api.Tests/`.

## Capacidades

| Entidad | Alcance de este primer corte |
|---|---|
| Catálogo | Alta (identidad estable, código único por tenant) + obtención + listado paginado. |
| CatalogoVersion | Alta en borrador con sus ítems (número incremental automático) + publicación (RBAC + ABAC, cierra automáticamente la vigencia de la versión previamente vigente del mismo catálogo). Emite `CatalogoVersionPublicadaIntegrationEvent` al publicar. |
| CatalogoItem | Carga junto con la versión en borrador que los contiene — sin alta/edición individual posterior a la creación de la versión (ver "Pendientes"). |
| Parametro | Alta (identidad estable, código único por tenant) + obtención + listado paginado. |
| ParametroVigencia | Alta con validación explícita de no-solapamiento con vigencias existentes del mismo parámetro (`Result.Failure` → 409, nunca una excepción) + listado. Emite `ParametroVigenciaCreadaIntegrationEvent`. |

El Plan Maestro exige, para este módulo, priorizar Catálogos versionados sobre Parámetros si el tiempo
no alcanza para ambos con la misma calidad. Este corte entrega **ambos** con vertical slice completo y
evidencia real de ejecución (ver "Pruebas"), pero Catálogos versionados recibió el corte más elaborado
(publicación con cierre automático de vigencia anterior, consulta de ítems vigentes a una fecha dada) —
Parámetros es deliberadamente más simple (sin "cerrar" una vigencia después de creada, ver "Pendientes").

## Modelo de versionado (Catálogos)

```
Catalogo (identidad estable, código único por tenant)
  └─ CatalogoVersion (número incremental, Estado: Borrador → Publicada, VigenteDesde/VigenteHasta)
        └─ CatalogoItem (Codigo, Etiqueta, Valor, Orden)
```

- `Catalogo` nunca cambia una vez creado (código y nombre descriptivo) — toda evolución del contenido
  vive en una `CatalogoVersion` nueva. Una referencia externa a una `CatalogoVersionId` concreta (por
  ejemplo, otro módulo que guardó el Id de la versión vigente al momento de leerla) **nunca deja de
  resolver**, aunque se publique una versión más nueva: publicar no borra ni modifica la versión
  anterior, solo cierra su vigencia (`CatalogoVersion.CerrarVigencia`).
- Una `CatalogoVersion` nace en `Borrador` (sus ítems todavía se pueden cargar en el mismo alta,
  `CrearCatalogoVersionCommand`) y pasa a `Publicada` de forma **irreversible**
  (`PublicarCatalogoVersionCommand`) — no existe un tercer estado ni un camino de "despublicar".
- Al publicar una versión nueva del mismo catálogo, si existe una versión previamente vigente (Estado
  `Publicada` con `VigenteHasta` todavía nula), su vigencia se cierra automáticamente
  (`VigenteHasta = VigenteDesde` de la versión nueva) — así nunca conviven dos versiones vigentes del
  mismo catálogo al mismo tiempo. Ver el test
  `PublicarSegundaVersion_CierraLaVigenciaDeLaAnterior`.
- Deliberadamente **no** es un motor de versionado semántico completo (el Plan Maestro solo exige "un
  número de versión incremental y fechas de vigencia") — `Numero` es un entero incremental por
  catálogo (1, 2, 3, ...), sin semver.

## Modelo de vigencia (Parámetros)

- `Parametro` nunca guarda un valor propio — el valor vigente en un momento dado se resuelve
  consultando sus `ParametroVigencia` (`ObtenerValorVigenteQuery`).
- `ParametroVigencia.VigenteHasta` nula significa "vigente indefinidamente hacia adelante". Dos
  vigencias del MISMO parámetro **nunca se solapan en el tiempo** — validado explícitamente por
  `CrearParametroVigenciaCommandHandler` (`VigenciasSuperpuestasSpecification`) ANTES de persistir:
  es un error de negocio esperado (`Result.Failure` → 409 `Catalogos.Parametros.VigenciaSuperpuesta`),
  nunca una excepción ni una restricción de base de datos que dependa de una carrera.
- La consulta real exigida por el Plan Maestro (`ObtenerValorVigenteQuery`, parámetro `fecha` opcional,
  default `DateTime.UtcNow`) resuelve la vigencia cuyo rango `[VigenteDesde, VigenteHasta)` cubre la
  fecha pedida — nunca "traer todas las vigencias y resolver en el cliente".

## Modelo de datos

`CatalogsDbContext` (hereda de `MultiTenantDbContext`, Shared.Infrastructure.Persistence) es el dueño
exclusivo de las tablas `Catalogos`, `CatalogoVersiones`, `CatalogoItems`, `Parametros`,
`ParametroVigencias` (más `IdempotencyKey`/`OutboxMessage`/`InboxMessage`, configuradas
automáticamente por la clase base). Ningún otro módulo de plataforma debe leer/escribir estas tablas
directamente.

- `Catalogo` (`AggregateRoot<Guid>`, `ITenantEntity`, `IAuditedEntity`, `ISoftDelete`): `Codigo` (único
  por tenant), `Nombre`, `Descripcion`.
- `CatalogoVersion` (`AggregateRoot<Guid>`, `ITenantEntity`, `IAuditedEntity`): `CatalogoId`, `Numero`,
  `Estado` (`Borrador`/`Publicada`), `VigenteDesde`, `VigenteHasta`, `PublicadaAtUtc`.
- `CatalogoItem` (`Entity<Guid>`, `ITenantEntity`): `CatalogoVersionId`, `Codigo`, `Etiqueta`, `Valor`,
  `Orden` — no es `AggregateRoot`: su ciclo de vida está atado por completo a la versión que lo
  contiene, sin eventos propios.
- `Parametro` (`AggregateRoot<Guid>`, `ITenantEntity`, `IAuditedEntity`, `ISoftDelete`): `Codigo`
  (único por tenant), `Nombre`, `Descripcion`.
- `ParametroVigencia` (`AggregateRoot<Guid>`, `ITenantEntity`, `IAuditedEntity`): `ParametroId`,
  `Valor`, `VigenteDesde`, `VigenteHasta`.

Ningún `DbContext` se expone como `IQueryable` fuera del repositorio (regla dura,
`docs/convenciones.md`): toda lectura pasa por `IReadRepository<TEntity,TId>`/`ISpecification<TEntity>`
(F1-17/F1-18) — ver `Catalogos/CatalogoSpecifications.cs`/`Parametros/ParametroSpecifications.cs`.

## Cómo consumirlo desde un host

```csharp
// InfrastructureModule.cs del host consumidor
services.AddHttpContextTenantProvider();               // F1-12, antes de AddSharedPersistence
services.AddSharedPersistence<CatalogsDbContext>(connectionString);

// El host necesita su PROPIA infraestructura de identidad/RBAC (Catalogs no es dueño de usuarios/
// roles) -- normalmente compuesta junto con Identity Administration (Fase 6, módulo 1) en un
// consumidor real. Ver Sample.Catalogs.Api para un DbContext de identidad mínimo de referencia.
services.AddSharedSecurity<ApplicationUser, ApplicationRole, TIdentityDbContext>(configuration);

// F2-08/F2-10: ABAC -- requerido por PublicarCatalogoVersionCommandHandler. Un consumidor real
// configura aquí su propia regla de alcance sobre "catalogos.versiones" (ver la sección de abajo).
services.AddSharedAbacAuthorization(options =>
{
    options.ScopeRules.Add(new AbacScopeAttributeRule
    {
        ResourceType = "catalogos.versiones",
        ResourceAttributeKey = "catalogoId",
        ClaimType = "catalogo_id",
    });
});

services.AddSharedAuditing();          // F2-15
services.AddSharedCatalogs();          // actor de auditoría/ABAC + health check propio

services.AddHttpContextIdempotencyKeyProvider();   // F1-22, antes de AddSharedApplication
services.AddSharedApplication(
    typeof(InfrastructureModule).Assembly,
    typeof(CatalogsDbContext).Assembly);        // handlers/validators del módulo, ensamblado propio
services.AddSharedExceptionHandling();

services.AddSharedApiVersioning();
services.AddSharedOpenApiForApiVersion(1, options => options.AddJwtBearerSecurityScheme());
```

```csharp
// Módulo del host que activa los endpoints (mismo patrón que Organization)
[DependsOn(typeof(InfrastructureModule))]
public class CatalogsApiModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

    public void ConfigureApplication(WebApplication app) => app.MapCatalogsEndpoints();
}
```

Ejemplo ejecutable completo: `samples/Sample.Catalogs.Api/` (`InfrastructureModule.cs`,
`CatalogsApiModule.cs`, `Program.cs`) y sus tests de integración en
`samples/Sample.Catalogs.Api.Tests/Integration/CatalogsEndpointsIntegrationTests.cs`.

## Endpoints

### Catálogos (`/api/v1/catalogos/...`)

| Método y ruta | Permiso RBAC | ABAC adicional |
|---|---|---|
| `POST /` | `catalogos.catalogos.crear` | — |
| `GET /{id}` | `catalogos.catalogos.ver` | — |
| `GET /` (paginado) | `catalogos.catalogos.ver` | — |
| `POST /{catalogoId}/versiones` | `catalogos.versiones.crear` | — |
| `GET /{catalogoId}/items-vigentes?fecha=` | `catalogos.catalogos.ver` | — |
| `POST /versiones/{id}/publicar` | `catalogos.versiones.publicar` | `AttributeScopeAbacRule` sobre `catalogoId` |

### Parámetros (`/api/v1/parametros/...`)

| Método y ruta | Permiso RBAC | ABAC adicional |
|---|---|---|
| `POST /` | `catalogos.parametros.crear` | — |
| `GET /{id}` | `catalogos.parametros.ver` | — |
| `GET /` (paginado) | `catalogos.parametros.ver` | — |
| `POST /{parametroId}/vigencias` | `catalogos.parametros.vigencias.crear` | — |
| `GET /{parametroId}/vigencias` | `catalogos.parametros.vigencias.ver` | — |
| `GET /{parametroId}/valor-vigente?fecha=` | `catalogos.parametros.ver` | — |

Todas las mutaciones (`POST`) exigen un header `Idempotency-Key` (F1-22, `IIdempotentCommand`): un
reintento con la misma clave y el mismo cuerpo devuelve el mismo resultado sin duplicar el efecto.

## RBAC + ABAC en la publicación de versiones (requisito común de Fase 6)

El caso de referencia del módulo para "RBAC y ABAC en operaciones sensibles": publicar una versión que
otros módulos pueden empezar a consumir de inmediato exige `catalogos.versiones.publicar` -- un permiso
**distinto y más restrictivo** que `catalogos.versiones.crear` (crear un borrador es de menor alcance
que hacerlo visible como vigente). Además, `PublicarCatalogoVersionCommandHandler` evalúa explícitamente
`IAuthorizationPolicyEvaluator` (F2-08) con la regla ABAC **incorporada** del framework,
`AttributeScopeAbacRule` -- mismo patrón que `DesactivarEmpresaCommandHandler` (Organization, Fase 6
módulo 2): un consumidor real limita qué catálogos puede publicar un actor cuyo rol solo administra un
subconjunto de catálogos del mismo tenant, configurando `AbacOptions.ScopeRules` con
`ResourceType = "catalogos.versiones"`, `ResourceAttributeKey = "catalogoId"` y el `ClaimType` que
transporte el alcance del actor (por ejemplo, `"catalogo_id"`). Sin ningún claim de ese tipo, la regla
no restringe (política de "sin dato, no se restringe") -- así que un actor de plataforma sin ese claim
(superadministrador) puede publicar cualquier catálogo, mientras que un actor con el claim limitado solo
puede publicar los catálogos que ese claim habilita, aunque tenga el permiso RBAC.

`Sample.Catalogs.Api` (el host de referencia) **no** registra ninguna regla de alcance por defecto
(`AbacOptions.ScopeRules` vacío) -- es responsabilidad de cada consumidor real decidir su propio
criterio de alcance. El test
`PublicarVersion_FueraDeAlcanceAbac_EsDenegadaAunqueTengaElPermisoRbac`
(`samples/Sample.Catalogs.Api.Tests/Integration/CatalogsEndpointsIntegrationTests.cs`) configura esa
regla explícitamente vía `WithWebHostBuilder`, solo para demostrar el mecanismo de punta a punta.

## Auditoría

Toda mutación (`CrearCatalogoCommand`, `CrearCatalogoVersionCommand`, `PublicarCatalogoVersionCommand`,
`CrearParametroCommand`, `CrearParametroVigenciaCommand` en su camino exitoso) escribe una entrada vía
`IAuditWriter` (F2-15) con actor, tenant, acción, recurso y resultado -- mismo patrón que Organization.
Un rechazo de negocio esperado (código duplicado, vigencia solapada, versión ya publicada) **no** se
audita como `Denied`/`Error` (esas categorías de `AuditOutcome` están reservadas a decisiones de
autorización RBAC/ABAC denegadas y a fallos técnicos inesperados respectivamente) -- mismo criterio ya
establecido por `DesactivarEmpresaCommandHandler` (Organization), que tampoco audita su camino
`NotFound`.

## Eventos de dominio e integración

`CatalogoVersionPublicadaIntegrationEvent` (`Catalogos.CatalogoVersionPublicada`) y
`ParametroVigenciaCreadaIntegrationEvent` (`Catalogos.ParametroVigenciaCreada`) implementan
DELIBERADAMENTE tanto `DomainEvent` (para que `OutboxSaveChangesInterceptor`, F1-23, los recolecte)
como `IIntegrationEvent` (para que `OutboxBatchProcessor`, F3-03, los publique) e `IHasPartitionKey`
(partición por `CatalogoId`/`ParametroId` respectivamente, F3-05) -- mismo patrón que los eventos de
Organization. Registrados en `docs/catalogo-eventos.md` (regla dura 27). Ningún host de referencia de
este repositorio (`samples/Sample.Catalogs.Api`) los publica hoy contra un broker Kafka productivo real
(no registra `AddSharedKafkaEventing`) -- quedan en la tabla `OutboxMessage`, sin relay activo; el
mecanismo de publicación en sí ya está probado de punta a punta por `samples/Sample.Eventing` (F3-13),
mismo criterio documentado por `docs/guia-organization.md`.

## Pendientes explícitos

Honestidad sobre el alcance de este primer corte (mismo criterio que Organization documentó los
suyos):

1. **`CatalogoItem` sin alta/edición individual posterior.** Los ítems se cargan únicamente junto con
   la versión en borrador que los contiene (`CrearCatalogoVersionCommand`) -- no hay
   `AgregarItemCommand`/`EditarItemCommand` para modificar una versión ya creada (aunque siga en
   borrador). Si un consumidor real necesita cargar ítems incrementalmente, es una extensión natural.
2. **`ParametroVigencia` sin "cerrar" una vigencia después de creada.** Una vigencia con
   `VigenteHasta = null` (vigente indefinidamente) solo deja de estarlo cuando una vigencia POSTERIOR
   no solapada se crea y la fecha de consulta cae dentro del rango de la nueva -- no existe un
   `CerrarVigenciaCommand` explícito para acortar retroactivamente una vigencia ya abierta (a
   diferencia de `CatalogoVersion.CerrarVigencia`, que sí existe porque `PublicarCatalogoVersionCommand`
   lo necesita internamente).
3. **Sin validación de formato en `CatalogoItem.Valor`/`ParametroVigencia.Valor`.** Ambos son `string`
   libres (hasta 500 caracteres) -- sin tipado fuerte (numérico, fecha, booleano) ni validación
   específica por catálogo/parámetro. Un consumidor real que necesite, por ejemplo, que "TasaIVA" sea
   siempre un decimal válido debe validarlo en su propia capa de aplicación.
4. **Condición de carrera real en la validación de no-solapamiento de `ParametroVigencia`.**
   `CrearParametroVigenciaCommandHandler` valida con un `AnyAsync(VigenciasSuperpuestasSpecification)`
   (check) y recién después inserta (act), sin ningún guardrail a nivel de datos (constraint de
   exclusión, índice único por rango, o transacción con aislamiento serializable) y sin que el comando
   sea `ITransactionalCommand` -- corre bajo READ COMMITTED por defecto. Dos requests concurrentes
   creando vigencias solapadas del mismo `ParametroId` pueden ambos pasar el `AnyAsync` antes de que
   cualquiera persista, resultando en dos vigencias solapadas guardadas -- exactamente el escenario que
   la validación pretende evitar, pero que no evita bajo concurrencia real. Hallazgo real de la
   auditoría de arquitectura de este módulo, no corregido en este corte porque exige decidir entre un
   guardrail de datos (constraint de exclusión de PostgreSQL no aplica a SQL Server; alternativa:
   columna computada + índice filtrado, o `SELECT ... WITH (UPDLOCK, HOLDLOCK)`) o promover el comando a
   `ITransactionalCommand` con aislamiento serializable -- ambas opciones tienen costo de lock
   distinto y merecen decidirse con datos de concurrencia real esperada, no a ciegas.
5. **Sin endpoint de "listar todas las versiones de un catálogo" ni "obtener una versión por número".**
   Solo existe la consulta de la versión vigente a una fecha (`ListarItemsVersionVigenteQuery`) -- un
   consumidor que necesite auditar el historial completo de versiones de un catálogo no tiene, todavía,
   un endpoint dedicado (aunque los datos están completos en `CatalogoVersiones`).
6. **Sin `dotnet ef migrations` real.** Mismo estado que el resto del repositorio (`EnsureCreatedAsync`
   en `Program.cs` de `Sample.Catalogs.Api`, sin migraciones versionadas) -- no es deuda nueva de este
   módulo.
7. **Sin publicación real contra un broker Kafka productivo.** Ver sección "Eventos de dominio e
   integración" arriba.
8. **Host de referencia con dos bases de datos separadas.** Mismo patrón que
   `Sample.Organization.Api` -- `CatalogsDbContext` (`ConnectionStrings:Default`) y un
   `SampleIdentityDbContext` propio (`ConnectionStrings:Identity`) solo para poder emitir JWTs reales en
   los tests.

## Pruebas

`samples/Sample.Catalogs.Api.Tests/Integration/CatalogsEndpointsIntegrationTests.cs` -- contra SQL
Server real (Testcontainers, `SqlServerContainerFixture`), vía `WebApplicationFactory<Program>`:

- Alta de catálogo sin autenticación → 401.
- Alta de catálogo con permiso → 201 + idempotencia (misma `Idempotency-Key` + mismo cuerpo → mismo Id).
- Alta de catálogo con código duplicado → 409.
- Vertical slice completo: alta de catálogo → alta de versión en borrador con ítems → publicación →
  lectura de ítems de la versión vigente (consulta real, no solo CRUD).
- Publicar una segunda versión cierra automáticamente la vigencia de la primera -- verificado leyendo
  los ítems vigentes antes y después del corte de vigencia.
- Publicación de versión fuera del alcance ABAC configurado (`catalogo_id`) → 403, aunque el actor
  tenga el permiso RBAC `catalogos.versiones.publicar`.
- Alta de parámetro con permiso → 201.
- Alta de vigencia solapada con una vigencia existente del mismo parámetro → 409
  (`Catalogos.Parametros.VigenciaSuperpuesta`).
- Consulta del valor vigente de un parámetro a distintas fechas, con dos vigencias consecutivas no
  solapadas -- resuelve la vigencia correcta en cada caso.

Comando de verificación real (evidencia de ejecución en el reporte de cierre de esta tarea):

```powershell
dotnet test samples/Sample.Catalogs.Api.Tests/Sample.Catalogs.Api.Tests.csproj
```

Resultado real de la última ejecución: 9 pruebas, 9 correctas, 0 fallidas (contra SQL Server real vía
Testcontainers).
