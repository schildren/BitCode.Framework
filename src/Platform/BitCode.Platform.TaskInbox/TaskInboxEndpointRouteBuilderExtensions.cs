using Asp.Versioning;
using BitCode.Framework.Platform.TaskInbox.Bandeja;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.TaskInbox;

/// <summary>
/// Mapea los endpoints HTTP del módulo Task Inbox bajo <c>/api/v1/taskinbox/...</c>. Deliberadamente
/// NO expone ningún endpoint que resuelva/apruebe/rechace/delegue una tarea -- esas mutaciones siguen
/// siendo responsabilidad exclusiva de Workflow (<c>POST /api/v1/workflows/tareas/{id}/resolver</c>,
/// <c>.../delegar</c>). Un cliente real usa los endpoints de acá para DESCUBRIR/FILTRAR su bandeja y
/// los de Workflow para ACTUAR sobre una tarea -- ver <c>docs/guia-taskinbox.md</c>.
/// </summary>
public static class TaskInboxEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapTaskInboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints.MapGroup("/api/v{version:apiVersion}/taskinbox/bandeja")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        group.MapGet("/", async (
            ISender sender, CancellationToken ct,
            TaskInboxEstado? estado = null, Guid? workflowInstanceId = null,
            DateTime? desdeUtc = null, DateTime? hastaUtc = null, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(
                new ListarBandejaQuery(estado, workflowInstanceId, desdeUtc, hastaUtc, page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(TaskInboxPermissions.BandejaVer)
            .Produces<PagedResult<TaskInboxItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerItemBandejaQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(TaskInboxPermissions.BandejaVer)
            .Produces<TaskInboxItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/marcar-leida", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new MarcarComoLeidaCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(TaskInboxPermissions.BandejaMarcarLeida)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        return endpoints;
    }
}
