using Asp.Versioning;
using MyApp.Modules.Elementos;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MyApp.Modules;

/// <summary>
/// Mapea los endpoints HTTP de este módulo bajo <c>/api/v{version}/modulename/elementos</c> (mismo
/// patrón que <c>src/Platform/BitCode.Platform.Dashboard/DashboardEndpointRouteBuilderExtensions.cs</c>).
/// Todos los ProblemDetails (400/404) usan el schema estándar de <c>ResultExtensions.ToProblemDetails</c>
/// (RFC 7807) -- ver docs/guia-openapi.md. Los endpoints de escritura llaman
/// <c>.RequireAuthorization(ModuleNamePermissions.*)</c>; para que ese nombre de policy resuelva en
/// runtime, el host debe registrarlo (ver README.md, sección RBAC).
/// </summary>
public static class ModuleNameEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapModuleNameEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var elementos = endpoints.MapGroup("/api/v{version:apiVersion}/modulename/elementos")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        elementos.MapPost("/", async (CrearElementoCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/modulename/elementos/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(ModuleNamePermissions.ElementosAdministrar)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        elementos.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerElementoQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(ModuleNamePermissions.ElementosVer)
            .Produces<ElementoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Ningún endpoint de listado queda ilimitado -- page/pageSize crudos del cliente se validan
        // dentro del handler (PageRequest.Create, límite máximo configurable, 100 por defecto) antes de
        // tocar el repositorio.
        elementos.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarElementosQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(ModuleNamePermissions.ElementosVer)
            .Produces<PagedResult<ElementoResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
