# Guía — Versionado de API HTTP (F1-27)

## Qué problema resuelve

Un endpoint HTTP sin versión no puede cambiar de contrato (agregar un campo requerido, cambiar el
tipo de una respuesta, eliminar un campo) sin romper a todos los consumidores existentes en el mismo
despliegue. `docs/politica-versionado.md` (sección 4, F0-05) ya definió el mecanismo elegido —
segmento de ruta, `/api/v{mayor}/...` — como propuesta; esta tarea (F1-27) lo implementa: agrega el
paquete de versionado, lo conecta al patrón de módulos (`IWebFrameworkModule`) del framework, y migra
`samples/Sample.Api` como referencia de uso end-to-end con dos versiones coexistiendo.

## Mecanismo y paquete

- **Paquete:** [`Asp.Versioning.Http`](https://github.com/dotnet/aspnet-api-versioning) (sucesor
  mantenido por la comunidad .NET de `Microsoft.AspNetCore.Mvc.Versioning`). Solo el paquete `.Http`
  hace falta: el repositorio usa Minimal API (`MapGroup`/`MapGet`/`MapPost`) en todos sus proyectos
  Web, no controllers MVC, así que `Asp.Versioning.Mvc.ApiExplorer` no aplica. Referenciado desde
  `Shared.Infrastructure.Web` (no desde cada proyecto consumidor): al ser `PackageReference` de un
  proyecto que Sample.Api ya referencia vía `ProjectReference`, la superficie de extensión de
  `Asp.Versioning.Builder` (`NewApiVersionSet`, `WithApiVersionSet`, `HasApiVersion`,
  `MapToApiVersion`, `HasDeprecatedApiVersion`) queda disponible sin que el consumidor declare su
  propio `PackageReference`.
- **Mecanismo:** versionado por segmento de ruta (`UrlSegmentApiVersionReader`), nunca por header ni
  media type — ver `docs/politica-versionado.md`, sección 4, para las razones ya documentadas
  (visibilidad en logs/trazas/gateway, compatibilidad directa con `MapGroup`). Esta tarea no
  encontró ninguna razón técnica para apartarse de esa propuesta: se adopta tal cual.
- Solo el número **mayor** viaja en la ruta (`v1`, `v2`, nunca `v1.2`), coherente con la política.

## Cómo registrar el versionado en un proyecto consumidor

```csharp
// InfrastructureModule.cs
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // ...
    services.AddSharedApiVersioning(); // Shared.Infrastructure.Web (F1-27)
}
```

`AddSharedApiVersioning()` (`WebServiceCollectionExtensions`) configura:

- `ApiVersionReader = new UrlSegmentApiVersionReader()` — la versión viaja en la ruta, nunca en un
  header/`Accept` que el cliente pueda omitir en silencio.
- `ReportApiVersions = true` — toda respuesta de un endpoint versionado incluye los headers
  `api-supported-versions`/`api-deprecated-versions` (ver sección "Headers" más abajo).
- `AssumeDefaultVersionWhenUnspecified = false` — como la versión siempre viaja en la ruta, un
  request sin segmento de versión simplemente no matchea ningún endpoint mapeado (404 de
  enrutamiento), en vez de asumir en silencio una versión por defecto.

## Cómo versionar un endpoint nuevo

Cada módulo de feature (`IWebFrameworkModule`) construye su propio `ApiVersionSet` y lo aplica al
grupo de rutas del feature — ver `samples/Sample.Api/Productos/ProductosModule.cs` como referencia
completa:

```csharp
public void ConfigureApplication(WebApplication app)
{
    var apiVersionSet = app.NewApiVersionSet()
        .HasApiVersion(new ApiVersion(1))
        .HasApiVersion(new ApiVersion(2))
        .ReportApiVersions()
        .Build();

    var productos = app.MapGroup("/api/v{version:apiVersion}/productos")
        .WithApiVersionSet(apiVersionSet);

    // v1: contrato original.
    productos.MapGet("/{id:guid}", ObtenerProductoV1Handler).HasApiVersion(new ApiVersion(1));

    // v2: mismo path relativo, handler y contrato de respuesta DISTINTOS -- coexiste con v1 sin
    // reemplazarlo ni requerir tocarlo.
    productos.MapGet("/{id:guid}", ObtenerProductoV2Handler).MapToApiVersion(new ApiVersion(2));
}
```

Reglas:

1. **Un cambio retrocompatible (campo opcional nuevo, endpoint nuevo, query param opcional) no
   incrementa la versión de ruta** — se agrega directo al endpoint existente (política de
   versionado, sección 4).
2. **Un cambio breaking se publica bajo una nueva ruta de versión mayor, junto a la anterior, nunca
   reemplazándola.** El endpoint viejo (`.HasApiVersion(new ApiVersion(1))`) sigue registrado sin
   cambios; el nuevo (`.MapToApiVersion(new ApiVersion(2))`) es un mapeo adicional del mismo template
   de ruta con handler/contrato propio.
3. Un `IQuery`/`ICommand` nuevo para la versión nueva sigue las mismas reglas duras de
   `docs/convenciones.md` que cualquier otro (nomenclatura, `IReadRepository` para queries,
   `ToOkOrProblem`, etc.) — versionar no exime ninguna regla existente.
4. Cada `ApiVersionSet` es por grupo de rutas de un feature (no uno global para todo `Sample.Api`):
   un feature puede evolucionar su propio ciclo de versiones sin acoplarse al de otro.

## Cómo deprecar una versión vieja

```csharp
var apiVersionSet = app.NewApiVersionSet()
    .HasDeprecatedApiVersion(new ApiVersion(1)) // en vez de HasApiVersion
    .HasApiVersion(new ApiVersion(2))
    .ReportApiVersions()
    .Build();
```

`HasDeprecatedApiVersion` **no** saca la versión de servicio ni cambia su comportamiento: v1 sigue
respondiendo con normalidad (los endpoints mapeados con `.HasApiVersion(new ApiVersion(1))` no
necesitan ningún cambio). Lo único que cambia es el contenido de los headers de reporte (ver debajo)
y el registro documentado en `CHANGELOG`/notas de versión, ya exigido por
`docs/politica-versionado.md`, sección 3 (política de deprecación general).

**Cuánto tiempo de coexistencia esperar antes de retirar la versión vieja:** la misma regla que
`docs/politica-versionado.md` ya define en su sección 3 para cualquier contrato público — período de
gracia mínimo de una versión mayor completa, extendido a la ventana de coexistencia acordada
explícitamente con los consumidores conocidos para contratos de alto radio de consumo (API pública).

La eliminación física del endpoint deprecado (quitar el `.HasApiVersion`/`.MapToApiVersion` de esa
versión) es un breaking change real y requiere el mismo proceso de aprobación de la sección 13 del
Plan Maestro — nunca se retira solo porque venció un plazo calendario sin confirmar que no quedan
consumidores.

## Headers de versión (aclaración importante frente a la propuesta original)

Con `ReportApiVersions = true`, toda respuesta de un endpoint que pertenece a un `ApiVersionSet`
incluye:

- `api-supported-versions`: lista de las versiones **no deprecadas** declaradas en ese
  `ApiVersionSet` (ej. `2`).
- `api-deprecated-versions`: lista de las versiones marcadas con `HasDeprecatedApiVersion` (ej. `1`).

**Estos son los headers reales que agrega Asp.Versioning** — verificados contra el servidor real en
`samples/Sample.Api.Tests/Integration/ApiVersioningIntegrationTests.cs`
(`ObtenerProducto_V1Deprecada_RespuestaIncluyeHeaderDeVersionesDeprecadas`). **No** son los headers
IETF `Sunset` (RFC 8594) ni `Deprecation` (borrador de IETF, sin RFC final al momento de escribir
esto) que a veces se mencionan como convención genérica de deprecación HTTP: Asp.Versioning no los
implementa. Un proyecto que necesite específicamente esos headers IETF (por ejemplo, para un gateway
o cliente que ya los interpreta) debe agregarlos con su propio middleware — no es parte del alcance
de F1-27 ni de este paquete.

## Qué pasa al pedir una versión que no existe

Verificado contra el servidor real (no asumido de antemano): pedir `/api/v99/productos/{id}` cuando
ningún endpoint mapeado declara la versión `99` responde **404 Not Found**, el 404 estándar de
enrutamiento de ASP.NET Core — no un `400 Bad Request` con `ProblemDetails` de
`"UnsupportedApiVersion"`. Esto es así porque, con `UrlSegmentApiVersionReader` +
`AssumeDefaultVersionWhenUnspecified = false` sobre Minimal API, el filtrado de versión ocurre en la
selección de endpoint candidato (Asp.Versioning descarta como candidatos los endpoints cuyo
`ApiVersionSet` no declara la versión pedida): si no queda ningún candidato para esa combinación de
ruta + versión, el resultado es indistinguible de pedir cualquier otra ruta inexistente. El 400 con
`ProblemDetails` de `"UnsupportedApiVersion"` sí es el comportamiento documentado de Asp.Versioning
para otros escenarios (por ejemplo, controllers MVC, o cuando `AssumeDefaultVersionWhenUnspecified`
está en juego) — no para este pipeline concreto. Ver
`ObtenerProducto_VersionNoDeclarada_RetornaErrorClaro_NoSilencioso` para el test que lo confirma.

## Coexistencia de versiones (criterio de aceptación literal de F1-27)

`samples/Sample.Api/Productos/ProductosModule.cs` mapea `GET /api/v1/productos/{id}` (contrato
original: `Id`, `Nombre`, `Precio`) y `GET /api/v2/productos/{id}` (agrega `CreadoEnUtc`) sobre el
mismo `ApiVersionSet` y el mismo grupo de rutas. Ambos endpoints están activos simultáneamente en el
mismo proceso: agregar v2 no requirió tocar ni un carácter del handler/contrato de v1
(`ObtenerProductoQuery`/`ProductoResponse` quedan intactos; v2 es un query/response nuevo,
`ObtenerProductoV2Query`/`ProductoResponseV2`). El test que lo demuestra de punta a punta, contra
SQL Server real (Testcontainers), es
`ApiVersioningIntegrationTests.ObtenerProducto_V1YV2_CoexistenSinRomperseMutuamente`
(`samples/Sample.Api.Tests/Integration/ApiVersioningIntegrationTests.cs`): crea un producto, pide la
misma entidad por v1 y por v2 en el mismo test, y verifica que ambas respuestas son 200 con su propio
contrato, sin que pedir una afecte a la otra.

## Referencias

- `docs/politica-versionado.md`, sección 4 — decisión de mecanismo (ahora "Implementada").
- `docs/convenciones.md` — regla dura (F1-27) y fila de la tabla "Cuándo usar qué".
- `src/Shared.Infrastructure.Web/WebServiceCollectionExtensions.cs` — `AddSharedApiVersioning`.
- `samples/Sample.Api/Productos/ProductosModule.cs` — `ApiVersionSet` de referencia (v1 deprecada +
  v2 coexistiendo).
- `samples/Sample.Api/Productos/ObtenerProductoV2Query.cs` — ejemplo de query/response de una
  versión nueva.
- `samples/Sample.Api.Tests/Integration/ApiVersioningIntegrationTests.cs` — verificación de punta a
  punta de coexistencia, error de versión inexistente y header de deprecación, contra SQL Server
  real.
