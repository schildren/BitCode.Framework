using Asp.Versioning;
using Asp.Versioning.Builder;
using BitCode.Framework.Platform.Catalogs.Catalogos;
using BitCode.Framework.Platform.Catalogs.Parametros;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.Catalogs;

/// <summary>
/// Mapea los endpoints HTTP del módulo Catalogs and Parameters bajo <c>/api/v1/catalogos/...</c> y
/// <c>/api/v1/parametros/...</c>. Un host consumidor llama a este método desde su propio módulo
/// (<c>IWebFrameworkModule</c> con <c>[DependsOn(typeof(InfrastructureModule))]</c>, en el ensamblado del
/// host). Los comandos/queries/DTOs que estos endpoints usan son <c>internal</c> a este ensamblado -- el
/// host nunca los referencia directamente, solo este único punto de entrada público. Ver
/// <c>docs/guia-catalogs.md</c>.
/// </summary>
public static class CatalogsEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapCatalogsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        MapCatalogosEndpoints(endpoints, apiVersionSet);
        MapParametrosEndpoints(endpoints, apiVersionSet);

        return endpoints;
    }

    private static void MapCatalogosEndpoints(IEndpointRouteBuilder endpoints, ApiVersionSet apiVersionSet)
    {
        var group = endpoints.MapGroup("/api/v{version:apiVersion}/catalogos")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        group.MapPost("/", async (CrearCatalogoRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CrearCatalogoCommand(body.Codigo, body.Nombre, body.Descripcion), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/catalogos/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(CatalogsPermissions.CatalogosCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerCatalogoQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(CatalogsPermissions.CatalogosVer)
            .Produces<CatalogoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarCatalogosQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(CatalogsPermissions.CatalogosVer)
            .Produces<PagedResult<CatalogoResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{catalogoId:guid}/versiones", async (Guid catalogoId, CrearCatalogoVersionRequest body, ISender sender, CancellationToken ct) =>
        {
            var items = body.Items
                .Select(i => new CatalogoItemInput(i.Codigo, i.Etiqueta, i.Valor, i.Orden))
                .ToList();
            var result = await sender.Send(new CrearCatalogoVersionCommand(catalogoId, items), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/catalogos/versiones/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(CatalogsPermissions.VersionesCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{catalogoId:guid}/items-vigentes", async (Guid catalogoId, ISender sender, CancellationToken ct, DateTime? fecha = null) =>
        {
            var result = await sender.Send(new ListarItemsVersionVigenteQuery(catalogoId, fecha), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(CatalogsPermissions.CatalogosVer)
            .Produces<IReadOnlyList<CatalogoItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var versiones = group.MapGroup("/versiones");

        versiones.MapPost("/{id:guid}/publicar", async (Guid id, PublicarCatalogoVersionRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new PublicarCatalogoVersionCommand(id, body.VigenteDesde, body.VigenteHasta), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(CatalogsPermissions.VersionesPublicar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapParametrosEndpoints(IEndpointRouteBuilder endpoints, ApiVersionSet apiVersionSet)
    {
        var group = endpoints.MapGroup("/api/v{version:apiVersion}/parametros")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        group.MapPost("/", async (CrearParametroRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CrearParametroCommand(body.Codigo, body.Nombre, body.Descripcion), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/parametros/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(CatalogsPermissions.ParametrosCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerParametroQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(CatalogsPermissions.ParametrosVer)
            .Produces<ParametroResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarParametrosQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(CatalogsPermissions.ParametrosVer)
            .Produces<PagedResult<ParametroResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{parametroId:guid}/vigencias", async (Guid parametroId, CrearParametroVigenciaRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CrearParametroVigenciaCommand(parametroId, body.Valor, body.VigenteDesde, body.VigenteHasta), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/parametros/{parametroId}/vigencias/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(CatalogsPermissions.VigenciasCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{parametroId:guid}/vigencias", async (Guid parametroId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListarVigenciasQuery(parametroId), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(CatalogsPermissions.VigenciasVer)
            .Produces<IReadOnlyList<ParametroVigenciaResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{parametroId:guid}/valor-vigente", async (Guid parametroId, ISender sender, CancellationToken ct, DateTime? fecha = null) =>
        {
            var result = await sender.Send(new ObtenerValorVigenteQuery(parametroId, fecha), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(CatalogsPermissions.ParametrosVer)
            .Produces<ParametroValorVigenteResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}

/// <summary>Cuerpo del POST de alta de catálogo.</summary>
internal sealed record CrearCatalogoRequest(string Codigo, string Nombre, string? Descripcion);

/// <summary>Cuerpo del POST de alta de versión en borrador -- separado del comando porque el binding de
/// Minimal API necesita un tipo propio para el body, distinto del comando que también carga el
/// <c>CatalogoId</c> tomado de la ruta.</summary>
internal sealed record CrearCatalogoVersionRequest(IReadOnlyList<CrearCatalogoVersionItemRequest> Items);

internal sealed record CrearCatalogoVersionItemRequest(string Codigo, string Etiqueta, string? Valor, int Orden);

/// <summary>Cuerpo del POST de publicación de una versión.</summary>
internal sealed record PublicarCatalogoVersionRequest(DateTime VigenteDesde, DateTime? VigenteHasta);

/// <summary>Cuerpo del POST de alta de parámetro.</summary>
internal sealed record CrearParametroRequest(string Codigo, string Nombre, string? Descripcion);

/// <summary>Cuerpo del POST de alta de vigencia de un parámetro.</summary>
internal sealed record CrearParametroVigenciaRequest(string Valor, DateTime VigenteDesde, DateTime? VigenteHasta);
