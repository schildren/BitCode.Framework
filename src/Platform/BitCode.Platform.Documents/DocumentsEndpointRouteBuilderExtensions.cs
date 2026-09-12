using Asp.Versioning;
using Asp.Versioning.Builder;
using BitCode.Framework.Platform.Documents.Documentos;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.Documents;

/// <summary>
/// Mapea los endpoints HTTP del módulo Documents bajo <c>/api/v1/documentos/...</c>. Un host consumidor
/// llama a este método desde su propio módulo (<c>IWebFrameworkModule</c> con
/// <c>[DependsOn(typeof(InfrastructureModule))]</c>, en el ensamblado del host). Los comandos/queries/DTOs
/// que estos endpoints usan son <c>internal</c> a este ensamblado -- el host nunca los referencia
/// directamente, solo este único punto de entrada público. Ver <c>docs/guia-documents.md</c>.
/// </summary>
public static class DocumentsEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints.MapGroup("/api/v{version:apiVersion}/documentos")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        // Carga inicial (multipart/form-data): IFormFile se materializa a byte[] ACÁ, en el borde HTTP --
        // CrearDocumentoCommand nunca recibe un Stream vivo (ver ese archivo para el motivo, compatibilidad
        // con IIdempotentCommand). .DisableAntiforgery() -- este host no registra el middleware de
        // antiforgery (API pura, sin formularios de navegador); el atributo automático que ASP.NET Core
        // agrega a todo endpoint con binding de formulario quedaría, si no se desactiva, esperando un
        // middleware que nunca se registra.
        group.MapPost("/", async (
            IFormFile archivo,
            [FromForm] string titulo,
            [FromForm] string? descripcion,
            [FromForm] string clasificacion,
            [FromForm] int retencionDias,
            ISender sender,
            CancellationToken ct) =>
        {
            var contenido = await LeerContenidoAsync(archivo, ct);
            var command = new CrearDocumentoCommand(
                titulo, descripcion, clasificacion, retencionDias, archivo.FileName, archivo.ContentType, contenido);
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/documentos/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .DisableAntiforgery()
            .RequireAuthorization(DocumentsPermissions.DocumentosCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerDocumentoQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(DocumentsPermissions.DocumentosVer)
            .Produces<DocumentoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarDocumentosQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(DocumentsPermissions.DocumentosVer)
            .Produces<PagedResult<DocumentoResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}/versiones", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListarVersionesDocumentoQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(DocumentsPermissions.DocumentosVer)
            .Produces<IReadOnlyList<DocumentoVersionResponse>>(StatusCodes.Status200OK);

        // Nueva versión sobre un documento existente -- mismo criterio de materialización a byte[] que el
        // alta inicial.
        group.MapPost("/{id:guid}/versiones", async (
            Guid id, IFormFile archivo, ISender sender, CancellationToken ct) =>
        {
            var contenido = await LeerContenidoAsync(archivo, ct);
            var command = new SubirVersionDocumentoCommand(id, archivo.FileName, archivo.ContentType, contenido);
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/documentos/{id}/versiones/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .DisableAntiforgery()
            .RequireAuthorization(DocumentsPermissions.DocumentosSubirVersion)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        // Descarga segura (Épica de Documents: "Carga y descarga segura") -- numero=null descarga la
        // versión vigente. Devuelve el contenido como archivo adjunto (Content-Disposition), nunca inline,
        // para que el navegador nunca intente renderizar contenido potencialmente hostil.
        group.MapGet("/{id:guid}/descargar", async (Guid id, int? numero, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DescargarDocumentoVersionQuery(id, numero), ct);
            return result.IsSuccess
                ? Results.File(result.Value.Contenido, result.Value.ContentType, result.Value.NombreArchivo)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(DocumentsPermissions.DocumentosDescargar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DisponerDocumentoCommand(id), ct);
            return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
        })
            .RequireAuthorization(DocumentsPermissions.DocumentosDisponer)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<byte[]> LeerContenidoAsync(IFormFile archivo, CancellationToken ct)
    {
        await using var memoryStream = new MemoryStream();
        await archivo.CopyToAsync(memoryStream, ct);
        return memoryStream.ToArray();
    }
}
