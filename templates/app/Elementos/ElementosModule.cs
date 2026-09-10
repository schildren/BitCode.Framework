using Asp.Versioning;
using Asp.Versioning.Builder;
using AppName;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using BitCode.Framework.Shared.Modularity;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AppName.Elementos;

[DependsOn(typeof(InfrastructureModule))]
public class ElementosModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app)
    {
        var apiVersionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var elementos = app.MapGroup("/api/v{version:apiVersion}/elementos")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        // Todos los ProblemDetails (400/404) usan el schema estándar de ResultExtensions.ToProblemDetails
        // (RFC 7807) -- nunca "error genérico sin tipar". Ver docs/guia-openapi.md.
        elementos.MapPost("/", async (CrearElementoCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/elementos/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        elementos.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerElementoQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .Produces<ElementoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Ningún endpoint de listado queda ilimitado -- page/pageSize crudos del cliente se validan
        // dentro del handler (PageRequest.Create, límite máximo configurable, 100 por defecto) antes de
        // tocar el repositorio.
        elementos.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarElementosQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .Produces<PagedResult<ElementoResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }
}
