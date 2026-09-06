using Asp.Versioning;
using Asp.Versioning.Builder;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Modularity;
using Sample.Api;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Api.Productos;

[DependsOn(typeof(InfrastructureModule))]
public class ProductosModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Sin registros propios: el repositorio genérico (Fase 1) y MediatR (Fase 2) ya cubren
        // todo lo que este feature necesita, registrados desde InfrastructureModule.
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

        productos.MapPost("/", async (CrearProductoCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/productos/{result.Value}", result.Value)
                : result.ToProblemDetails();
        }).HasApiVersion(new ApiVersion(1));

        // v1: contrato original (Id, Nombre, Precio) -- sigue existiendo sin cambios.
        productos.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerProductoQuery(id), ct);
            return result.ToOkOrProblem();
        }).HasApiVersion(new ApiVersion(1));

        // v2: mismo path relativo (/productos/{id}), handler y contrato de respuesta DISTINTOS
        // (agrega CreadoEnUtc) -- demuestra que agregar v2 no requiere tocar ni retirar v1.
        productos.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerProductoV2Query(id), ct);
            return result.ToOkOrProblem();
        }).MapToApiVersion(new ApiVersion(2));

        // F1-21: ningún endpoint de listado queda ilimitado -- page/pageSize llegan crudos del
        // cliente, pero ListarProductosQueryHandler los valida vía PageRequest.Create (límite máximo
        // configurable, 100 por defecto) antes de tocar el repositorio. Un pageSize fuera de rango
        // responde 400 con un ProblemDetails de validación, nunca "todas las filas".
        productos.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarProductosQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        }).HasApiVersion(new ApiVersion(1));
    }
}
