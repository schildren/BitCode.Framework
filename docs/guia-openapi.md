# Guía — Contrato OpenAPI (F1-28)

## Qué problema resuelve

Un endpoint HTTP sin contrato publicado obliga a cada consumidor a leer el código fuente (o probar a
ciegas) para saber qué campos manda, qué códigos de error puede recibir y qué forma tiene la
respuesta. F1-28 agrega la generación de ese contrato (OpenAPI 3.x) para los endpoints reales de
`samples/Sample.Api`, con las respuestas de error estandarizadas por el framework
(`ResultExtensions.ToProblemDetails`, RFC 7807) correctamente tipadas, un ejemplo real de
request/response en los dos endpoints principales, y un mecanismo de seguridad documentado solo
cuando el proyecto lo usa de verdad.

## Mecanismo y paquete

- **Paquete:** soporte **nativo** de .NET 10 (`Microsoft.AspNetCore.OpenApi`, `AddOpenApi`/
  `MapOpenApi`), no Swashbuckle ni NSwag. Cubre todo lo que pide esta tarea (generación,
  document/operation transformers para examples y seguridad, filtrado por documento) sin agregar una
  dependencia externa: Swashbuckle/NSwag solo agregarían valor para una UI visual de exploración
  (Swagger UI), que no es parte del alcance ni del criterio de aceptación ("Validación automática", no
  "UI navegable").
- Referenciado desde `Shared.Infrastructure.Web` (mismo patrón que `Asp.Versioning.Http`, F1-27): al
  ser `PackageReference` de un proyecto que `Sample.Api` ya referencia vía `ProjectReference`, la
  superficie de extensión queda disponible sin que el consumidor declare su propio
  `PackageReference`.
- Trae transitivamente `Microsoft.OpenApi` 2.x, el modelo de objetos del documento — **incluida su
  propia capacidad de lectura/validación** (`Microsoft.OpenApi.Reader.OpenApiDocument.Parse`), que es
  lo que la suite de tests usa para el criterio de aceptación "Validación automática" (ver más abajo).
  No hace falta `Microsoft.OpenApi.Readers` (el paquete de lectura clásico, v1.x): al momento de
  escribir esto no tiene ninguna versión estable compatible con `Microsoft.OpenApi` 2.x (solo
  prereleases `2.0.0-preview.*`, con breaking changes de API entre previews que rompen en tiempo de
  ejecución contra el `Microsoft.OpenApi` 2.7.5 que trae `Microsoft.AspNetCore.OpenApi` 10.0.11 —
  verificado empíricamente al evaluar esta tarea). El propio paquete `Microsoft.OpenApi` ya resuelve
  la lectura/validación por sí solo.

## Un documento OpenAPI por versión de API (F1-27)

`samples/Sample.Api` tiene dos versiones de API coexistiendo (`/api/v1/...`, `/api/v2/...`, F1-27).
Un único documento OpenAPI mezclando ambas oscurecería justo lo que F1-27 garantiza: dos contratos
distintos que conviven sin pisarse (por ejemplo, `GET /productos/{id}` tiene un contrato de respuesta
distinto en v1 y en v2 — mezclar ambos bajo el mismo `PathItem`/operación produciría un documento
ambiguo o, peor, uno de los dos contratos pisaría al otro).

Por eso F1-28 registra **un documento separado por versión mayor**:

```csharp
// InfrastructureModule.cs
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // ...
    services.AddSharedOpenApiForApiVersion(1); // -> /openapi/v1.json
    services.AddSharedOpenApiForApiVersion(2); // -> /openapi/v2.json
}

public void ConfigureApplication(WebApplication app)
{
    // ...
    app.MapOpenApi(); // un único MapOpenApi() resuelve TODOS los documentos registrados
}
```

`AddSharedOpenApiForApiVersion(majorVersion, configureOptions?)` (`OpenApiServiceCollectionExtensions`,
`Shared.Infrastructure.Web`) configura `OpenApiOptions.ShouldInclude` para que el documento `v{N}`
solo incluya las operaciones cuyo endpoint tiene `ApiVersionMetadata` (la metadata que
`Asp.Versioning.Http` adjunta con `.HasApiVersion(...)`/`.MapToApiVersion(...)`) mapeada a esa versión
mayor. Un endpoint sin esa metadata en absoluto, o marcado explícitamente `ApiVersionNeutral`, se
documenta en **todos** los documentos por versión (no es parte de un contrato de negocio versionado,
así que excluirlo de todos sería igual de incorrecto que incluirlo solo en uno).

**Límite real verificado, no supuesto:** `/health/live`/`/health/ready` (`MapHealthChecks`, F1-25) no
aparecen en NINGÚN documento generado, ni siquiera por la regla "sin versión aparece en todos". No es
un bug de `ShouldInclude`: `MapHealthChecks` mapea un `RequestDelegate` plano sin pasar por
`RequestDelegateFactory` (el mecanismo que anota la metadata de ApiExplorer a los delegados de
`MapGet`/`MapPost`/etc.), así que el generador nativo nunca llega a construir una `ApiDescription`
para esos dos endpoints — `ShouldInclude` ni siquiera se evalúa para ellos. Esto es, además,
consistente con su propósito: son endpoints operativos para un orquestador/balanceador, no parte del
contrato de negocio que consume un cliente de la API. Verificado en
`OpenApiDocumentsIntegrationTests.NingunDocumento_IncluyeLosEndpointsDeHealthChecks`.

## Errores estandarizados (RFC 7807, `ResultExtensions.ToProblemDetails`)

Un handler de `Sample.Api.Productos` siempre termina en `.ToOkOrProblem()`/`.ToProblemDetails()` sobre
un `Result` (regla dura #8 de `docs/convenciones.md`), que ya traduce todo error de negocio a
`ProblemDetails` (RFC 7807) con el status HTTP correspondiente al `ErrorType`. Pero un endpoint
Minimal API cuyo delegado retorna `Task<IResult>` (nunca `Results<Ok<T>, ProblemHttpResult>`, el tipo
de unión que sí permite inferencia automática) no le da al generador nativo forma de deducir por
reflexión qué status codes/schemas puede devolver — sin anotación explícita, el contrato solo listaría
un `200` genérico. Por eso cada endpoint de `ProductosModule` agrega explícitamente:

```csharp
productos.MapPost("/", async (CrearProductoCommand command, ISender sender, CancellationToken ct) => { /* ... */ })
    .HasApiVersion(new ApiVersion(1))
    .Produces<Guid>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest) // FluentValidation + Idempotency.KeyRequired
    .ProducesProblem(StatusCodes.Status409Conflict);            // Idempotency.KeyReused (F1-22)
```

`.ProducesValidationProblem(...)` documenta el schema de `HttpValidationProblemDetails` (el que emite
`TypedResults.ValidationProblem`, usado por `ToProblemDetails` para `ErrorType.Validation`);
`.ProducesProblem(...)` documenta el schema estándar de `ProblemDetails` (el que emite
`TypedResults.Problem`, usado para el resto de los `ErrorType`) — nunca "error genérico sin tipar".
Ver `samples/Sample.Api/Productos/ProductosModule.cs` para las cuatro anotaciones completas (crear,
obtener v1, obtener v2, listar) y el mapeo exacto código-de-error -> status HTTP que cada una
documenta (comentado en línea, con referencia a la regla/tarea de origen: F1-08, F1-21, F1-22).

## Seguridad: por qué `Sample.Api` NO documenta un esquema

`Sample.Api` no protege ningún endpoint con `RequireAuthorization(...)`/`[Authorize]` — es un proyecto
piloto/demo (ver el comentario ya existente sobre `EnsureCreatedAsync` en `Program.cs`), no un
consumidor real con autenticación activa. Describir en el contrato OpenAPI un esquema de seguridad
(`Bearer`/JWT) que el servidor no exige en ningún endpoint sería un contrato engañoso: un cliente que
confíe en ese contrato podría asumir, incorrectamente, que necesita un token para llamar a estos
endpoints, o peor, que el servidor efectivamente valida ese token cuando en realidad no lo hace.

El framework sí ofrece el mecanismo, listo para un consumidor real:
`OpenApiSecuritySchemeOptionsExtensions.AddJwtBearerSecurityScheme()` (`Shared.Infrastructure.Web`).
Un proyecto que proteja al menos un endpoint con autenticación JWT (`Shared.Infrastructure.Security`,
`JwtTokenGenerator`) lo encadena al configurar cada documento:

```csharp
services.AddSharedOpenApiForApiVersion(1, options => options.AddJwtBearerSecurityScheme());
```

Esto agrega el esquema `Bearer` (HTTP, JWT) a `components.securitySchemes` del documento, y marca como
requerido ese esquema (`security` a nivel de operación) **únicamente** en las operaciones cuyo
endpoint tiene metadata de autorización (`IAuthorizeData`) sin `AllowAnonymous` — el resto de las
operaciones del documento no se modifica. `Sample.Api` no lo llama porque, hoy, ninguna de sus
operaciones calificaría.

## Examples (request/response reales)

`ProductosExampleOperationTransformer` (`samples/Sample.Api/Productos/ProductosOpenApiExamples.cs`,
`IOpenApiOperationTransformer`) agrega un ejemplo real de negocio a los dos endpoints principales:

- `POST /api/v1/productos`: ejemplo de request body (`CrearProductoCommand`) y de la respuesta `201`
  (el `Guid` creado).
- `GET /api/v2/productos/{id}`: ejemplo de la respuesta `200` (`ProductoResponseV2`, incluido el campo
  `CreadoEnUtc` que v1 nunca expuso).

Vive en `samples/Sample.Api` y no en `Shared.Infrastructure.Web` a propósito: un ejemplo de negocio
concreto (nombre/precio de un producto) es contenido específico de este feature, no algo que el
framework pueda generalizar. Se registra por documento desde el propio feature module
(`ProductosModule.ConfigureServices`, vía `services.Configure<OpenApiOptions>("v1", ...)`), no desde
`InfrastructureModule` — que solo conoce documentos genéricos por versión
(`AddSharedOpenApiForApiVersion`), nunca un tipo concreto de un feature. `Configure<TOptions>` con el
mismo nombre de documento es aditivo: no reemplaza el `ShouldInclude` ya configurado por
`AddSharedOpenApiForApiVersion`.

## Cómo un endpoint nuevo debe anotar sus respuestas

Para que un endpoint nuevo aparezca correctamente documentado en el OpenAPI generado:

1. Anotar explícitamente cada respuesta posible con `.Produces<T>(statusCode)` (camino feliz) y
   `.ProducesProblem(statusCode)`/`.ProducesValidationProblem(statusCode)` (cada `ErrorType` que el
   `Result` de ese `IQuery`/`ICommand` puede devolver) — nunca asumir que el generador nativo lo infiere
   solo porque el endpoint usa `ToOkOrProblem()`/`ToProblemDetails()`.
2. Si el endpoint pertenece a un `ApiVersionSet` (F1-27), no hace falta nada adicional para que
   aparezca en el documento correcto: `AddSharedOpenApiForApiVersion` ya filtra por
   `ApiVersionMetadata`.
3. Un ejemplo de request/response real es opcional pero recomendado para los endpoints de mayor uso —
   agregar un `IOpenApiOperationTransformer` propio del feature (ver
   `ProductosExampleOperationTransformer`) y registrarlo con
   `services.Configure<OpenApiOptions>("v{N}", options => options.AddOperationTransformer<T>())`.
4. Si el endpoint requiere autenticación, encadenar `.AddJwtBearerSecurityScheme()` al configurar el
   documento (ver sección "Seguridad" arriba) — nunca dejar que el contrato sugiera un esquema que el
   servidor no aplica, ni omitirlo si el servidor sí lo aplica.

## Validación automática (criterio de aceptación literal de F1-28)

`OpenApiDocumentsIntegrationTests` (`samples/Sample.Api.Tests/Integration/`) levanta `Sample.Api`
completo (`WebApplicationFactory`, SQL Server real vía Testcontainers) y pide `/openapi/v1.json` y
`/openapi/v2.json` al servidor real — nunca un archivo estático mantenido a mano. Cada documento se
parsea con `Microsoft.OpenApi.Reader.OpenApiDocument.Parse(json, "json")`, que aplica las mismas
reglas de esquema que cualquier lector de OpenAPI de terceros (obligatoriedad de `info.version`, de al
menos una respuesta por operación, etc.) y devuelve un `ReadResult` con un `Diagnostic.Errors` real —
el test falla si esa lista no está vacía. Esto es lo que hace la validación "automática": no alcanza
con "el endpoint respondió 200 y el JSON parseó" (un JSON sintácticamente válido puede seguir siendo
un documento OpenAPI semánticamente inválido), ni con una inspección visual en un navegador.

Además de la validez estructural, la suite verifica:

- Que v1 documenta el contrato v1 de `GET /productos/{id}` (sin `CreadoEnUtc`) y v2 documenta el
  contrato v2 (con `CreadoEnUtc`) bajo el **mismo path relativo**, sin que ninguno de los dos filtre al
  documento del otro.
- Que `POST /api/v1/productos` documenta sus tres respuestas reales (`201`/`400`/`409`) y trae un
  ejemplo de request/response.
- Que ningún documento incluye `/health/live`/`/health/ready` (ver la sección de más arriba).

## Referencias

- `docs/convenciones.md` — regla dura (F1-28) y fila de la tabla "Cuándo usar qué".
- `src/Shared.Infrastructure.Web/OpenApi/OpenApiServiceCollectionExtensions.cs` —
  `AddSharedOpenApiForApiVersion`.
- `src/Shared.Infrastructure.Web/OpenApi/OpenApiSecuritySchemeOptionsExtensions.cs` —
  `AddJwtBearerSecurityScheme`.
- `samples/Sample.Api/Productos/ProductosModule.cs` — anotaciones `.Produces`/`.ProducesProblem` de
  referencia.
- `samples/Sample.Api/Productos/ProductosOpenApiExamples.cs` — transformer de ejemplos de referencia.
- `samples/Sample.Api.Tests/Integration/OpenApiDocumentsIntegrationTests.cs` — validación automática
  de punta a punta contra SQL Server real.
- `docs/guia-contratos-frontend.md` — cómo el frontend (`frontend/packages/*`) consume estos documentos
  OpenAPI para generar tipos TypeScript reales, en vez de mantener modelos escritos a mano sin ninguna
  verificación contra el contrato del backend.
