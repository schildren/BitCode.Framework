using Asp.Versioning;
using BitCode.Framework.Platform.Reporting.WorkflowInstancias;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.Reporting;

/// <summary>
/// Mapea los endpoints HTTP del módulo Reporting bajo <c>/api/v1/reporting/...</c>. Deliberadamente NO
/// expone ningún endpoint que mute <c>ReporteWorkflowInstancia</c> -- este módulo es puramente de lectura
/// y exportación sobre lo que sus consumidores de eventos ya escribieron, ver
/// <c>docs/guia-reporting.md</c>.
/// </summary>
public static class ReportingEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints.MapGroup("/api/v{version:apiVersion}/reporting/workflow-instancias")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        group.MapGet("/", async (
            ISender sender, CancellationToken ct,
            Guid? workflowDefinitionId = null, ReporteWorkflowInstanciaEstado? estado = null,
            DateTime? desdeUtc = null, DateTime? hastaUtc = null, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(
                new ListarReporteWorkflowInstanciasQuery(workflowDefinitionId, estado, desdeUtc, hastaUtc, page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(ReportingPermissions.WorkflowInstanciasVer)
            .Produces<PagedResult<ReporteWorkflowInstanciaResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerReporteWorkflowInstanciaQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(ReportingPermissions.WorkflowInstanciasVer)
            .Produces<ReporteWorkflowInstanciaResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/promedio-duracion", async (
            ISender sender, CancellationToken ct,
            Guid? workflowDefinitionId = null, DateTime? desdeUtc = null, DateTime? hastaUtc = null) =>
        {
            var result = await sender.Send(
                new ListarPromedioDuracionPorDefinicionQuery(workflowDefinitionId, desdeUtc, hastaUtc), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(ReportingPermissions.WorkflowInstanciasVer)
            .Produces<IReadOnlyList<PromedioDuracionPorDefinicionResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/exportar", async (
            ISender sender, CancellationToken ct,
            Guid? workflowDefinitionId = null, ReporteWorkflowInstanciaEstado? estado = null,
            DateTime? desdeUtc = null, DateTime? hastaUtc = null) =>
        {
            var result = await sender.Send(
                new ExportarReporteWorkflowInstanciasQuery(workflowDefinitionId, estado, desdeUtc, hastaUtc), ct);
            return result.IsSuccess
                ? Results.File(result.Value.Contenido, result.Value.ContentType, result.Value.NombreArchivo)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(ReportingPermissions.WorkflowInstanciasExportar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
