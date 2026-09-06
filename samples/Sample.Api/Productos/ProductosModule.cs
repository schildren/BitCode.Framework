using Asp.Versioning;
using Asp.Versioning.Builder;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using BitCode.Framework.Shared.Modularity;
using Sample.Api;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sample.Api.Productos;

[DependsOn(typeof(InfrastructureModule))]
public class ProductosModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // F1-28: agrega el transformer de ejemplos de request/response (ProductosExampleOperationTransformer)
        // a los documentos "v1"/"v2" que InfrastructureModule ya registró (AddSharedOpenApiForApiVersion) --
        // Configure<OpenApiOptions>(documentName, ...) es aditivo (no reemplaza el ShouldInclude ya
        // configurado), y desacopla Shared.Infrastructure.Web (genérico) de un tipo de Producto (específico
        // de este feature).
        services.Configure<OpenApiOptions>("v1", options => options.AddOperationTransformer<ProductosExampleOperationTransformer>());
        services.Configure<OpenApiOptions>("v2", options => options.AddOperationTransformer<ProductosExampleOperationTransformer>());
    }

    public void ConfigureApplication(WebApplication app)
    {
        // F1-27: convención de versionado de API HTTP (docs/politica-versionado.md, sección 4,
        // ahora "Implementada"; docs/guia-versionado-api.md). v1 se marca deprecada acá mismo como
        // referencia de uso del mecanismo (Sunset/Deprecation no son los headers reales que agrega
        // este paquete -- ver la guía --, pero el mecanismo de deprecación en sí queda demostrado de
        // punta a punta), sin que eso saque a v1 de servicio: sigue respondiendo con normalidad
        // mientras no se retire, coexistiendo con v2 (criterio de aceptación literal).
        var apiVersionSet = app.NewApiVersionSet()
            .HasDeprecatedApiVersion(new ApiVersion(1))
            .HasApiVersion(new ApiVersion(2))
            .ReportApiVersions()
            .Build();

        var productos = app.MapGroup("/api/v{version:apiVersion}/productos")
            .WithApiVersionSet(apiVersionSet);

        // F1-28: anotaciones explícitas de Minimal API (.Produces/.ProducesProblem/
        // .ProducesValidationProblem) para que el documento OpenAPI describa las respuestas de error
        // reales -- el handler retorna Task<IResult> (nunca Results<T1, T2, ...>), así que el
        // generador nativo no puede inferir status codes/schemas por reflexión del delegado; sin esta
        // anotación explícita, el contrato solo listaría un 200 genérico. Todos los ProblemDetails
        // (400/404/409) usan el schema estándar de ResultExtensions.ToProblemDetails (RFC 7807), nunca
        // "error genérico sin tipar".
        productos.MapPost("/", async (CrearProductoCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/productos/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .HasApiVersion(new ApiVersion(1))
            .Produces<Guid>(StatusCodes.Status201Created)
            // Idempotency.KeyRequired (falta el header Idempotency-Key) y errores de FluentValidation
            // (Nombre/Precio) -- ambos ErrorType.Validation, F1-22/piloto.
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            // Idempotency.KeyReused: misma Idempotency-Key con un body distinto (F1-22).
            .ProducesProblem(StatusCodes.Status409Conflict);

        // v1: contrato original (Id, Nombre, Precio) -- sigue existiendo sin cambios.
        productos.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerProductoQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .HasApiVersion(new ApiVersion(1))
            .Produces<ProductoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // v2: mismo path relativo (/productos/{id}), handler y contrato de respuesta DISTINTOS
        // (agrega CreadoEnUtc) -- demuestra que agregar v2 no requiere tocar ni retirar v1.
        productos.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerProductoV2Query(id), ct);
            return result.ToOkOrProblem();
        })
            .MapToApiVersion(new ApiVersion(2))
            .Produces<ProductoResponseV2>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // F1-21: ningún endpoint de listado queda ilimitado -- page/pageSize llegan crudos del
        // cliente, pero ListarProductosQueryHandler los valida vía PageRequest.Create (límite máximo
        // configurable, 100 por defecto) antes de tocar el repositorio. Un pageSize fuera de rango
        // responde 400 con un ProblemDetails de validación, nunca "todas las filas".
        productos.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarProductosQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .HasApiVersion(new ApiVersion(1))
            .Produces<PagedResult<ProductoResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }
}
