using BitCode.Framework.Platform.Dashboard.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Dashboard.Widgets;

/// <summary>
/// Detalle de UN widget del propio dashboard. Aplica el MISMO chequeo de ownership que
/// <see cref="ListarWidgetsQuery"/> -- corrige por diseño, desde el primer corte, el hallazgo Alto real de
/// Task Inbox (Fase 6, módulo 7): un endpoint de LISTADO ya filtrado por dueño no alcanza si el endpoint
/// de DETALLE por id no repite el mismo chequeo (IDOR).
/// </summary>
internal sealed record ObtenerWidgetQuery(Guid Id) : IQuery<DashboardWidgetResponse>;

internal sealed class ObtenerWidgetQueryHandler(
    IReadRepository<DashboardWidget, Guid> repository, IDashboardActorContext actorContext)
    : IRequestHandler<ObtenerWidgetQuery, Result<DashboardWidgetResponse>>
{
    public async Task<Result<DashboardWidgetResponse>> Handle(ObtenerWidgetQuery request, CancellationToken cancellationToken)
    {
        var widget = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (widget is null)
        {
            return Result.Failure<DashboardWidgetResponse>(Error.NotFound(
                "Dashboard.Widgets.NoEncontrado", $"No existe el widget {request.Id}."));
        }

        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure<DashboardWidgetResponse>(Error.Forbidden(
                "Dashboard.Widgets.ActorNoResuelto", "No se pudo resolver el actor autenticado."));
        }

        if (widget.UserId != actorUserId.Value)
        {
            return Result.Failure<DashboardWidgetResponse>(Error.Forbidden(
                "Dashboard.Widgets.NoAutorizado", "Solo el dueño del widget puede verlo."));
        }

        return Result.Success(new DashboardWidgetResponse(
            widget.Id, widget.Tipo, widget.Titulo, widget.WorkflowDefinitionId, widget.Orden));
    }
}
