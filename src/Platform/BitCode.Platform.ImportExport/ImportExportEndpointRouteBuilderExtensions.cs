using Asp.Versioning;
using BitCode.Framework.Platform.ImportExport.Exportacion;
using BitCode.Framework.Platform.ImportExport.Importacion;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.ImportExport;

/// <summary>Mapea los endpoints HTTP del módulo Import and Export bajo <c>/api/v1/importexport/...</c>.
/// Los comandos/queries/DTOs que estos endpoints usan son <c>internal</c> a este ensamblado -- el host
/// nunca los referencia directamente, solo este único punto de entrada público. Ver
/// <c>docs/guia-import-export.md</c>.</summary>
public static class ImportExportEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapImportExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints.MapGroup("/api/v{version:apiVersion}/importexport")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        MapImportaciones(group);
        MapExportaciones(group);

        return endpoints;
    }

    private static void MapImportaciones(RouteGroupBuilder group)
    {
        var importaciones = group.MapGroup("/importaciones");

        // multipart/form-data: IFormFile se materializa a byte[] ACÁ, en el borde HTTP -- mismo criterio
        // que DocumentsEndpointRouteBuilderExtensions (compatibilidad con IIdempotentCommand).
        // .DisableAntiforgery() -- este host no registra el middleware de antiforgery.
        importaciones.MapPost("/", async (
            IFormFile archivo, [Microsoft.AspNetCore.Mvc.FromForm] string tipoImportacion, ISender sender, CancellationToken ct) =>
        {
            var contenido = await LeerContenidoAsync(archivo, ct);
            var result = await sender.Send(new IniciarImportacionCommand(tipoImportacion, archivo.FileName, contenido), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/importexport/importaciones/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .DisableAntiforgery()
            .RequireAuthorization(ImportExportPermissions.ImportacionesIniciar)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        importaciones.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerImportJobQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(ImportExportPermissions.ImportacionesVer)
            .Produces<ImportJobResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        importaciones.MapGet("/{id:guid}/errores", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListarErroresImportJobQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(ImportExportPermissions.ImportacionesVer)
            .Produces<IReadOnlyList<ImportJobErrorResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        importaciones.MapGet("/", async (
            ISender sender, CancellationToken ct, string? tipoImportacion = null, ImportJobEstado? estado = null,
            int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarImportJobsQuery(tipoImportacion, estado, page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(ImportExportPermissions.ImportacionesVer)
            .Produces<PagedResult<ImportJobResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapExportaciones(RouteGroupBuilder group)
    {
        var exportaciones = group.MapGroup("/exportaciones");

        exportaciones.MapPost("/", async (IniciarExportacionRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new IniciarExportacionCommand(request.TipoExportacion, request.FiltroJson), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/importexport/exportaciones/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(ImportExportPermissions.ExportacionesIniciar)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        exportaciones.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerExportJobQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(ImportExportPermissions.ExportacionesVer)
            .Produces<ExportJobResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        exportaciones.MapGet("/{id:guid}/descargar", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DescargarResultadoExportJobQuery(id), ct);
            return result.IsSuccess
                ? Results.File(result.Value.Contenido, result.Value.ContentType, result.Value.NombreArchivo)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(ImportExportPermissions.ExportacionesVer)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        exportaciones.MapGet("/", async (
            ISender sender, CancellationToken ct, string? tipoExportacion = null, ExportJobEstado? estado = null,
            int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarExportJobsQuery(tipoExportacion, estado, page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(ImportExportPermissions.ExportacionesVer)
            .Produces<PagedResult<ExportJobResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<byte[]> LeerContenidoAsync(IFormFile archivo, CancellationToken ct)
    {
        await using var memoryStream = new MemoryStream();
        await archivo.CopyToAsync(memoryStream, ct);
        return memoryStream.ToArray();
    }

    private sealed record IniciarExportacionRequest(string TipoExportacion, string? FiltroJson);
}
