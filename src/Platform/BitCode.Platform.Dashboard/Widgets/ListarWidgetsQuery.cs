using BitCode.Framework.Platform.Dashboard.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Dashboard.Widgets;

/// <summary>El PROPIO dashboard del actor autenticado, ordenado por <see cref="DashboardWidget.Orden"/> --
/// no paginado a propósito, mismo criterio que <c>ListarPromedioDuracionPorDefinicionQuery</c> de
/// Reporting: el límite de <c>AgregarWidgetCommandHandler.MaximoWidgetsPorUsuario</c> (20) ya acota
/// estructuralmente el volumen de esta colección por diseño (regla dura 16, <c>docs/convenciones.md</c>:
/// "solo aceptable cuando el volumen está estructuralmente acotado").</summary>
internal sealed record ListarWidgetsQuery : IQuery<IReadOnlyList<DashboardWidgetResponse>>;

internal sealed class ListarWidgetsQueryHandler(
    IReadRepository<DashboardWidget, Guid> repository, IDashboardActorContext actorContext)
    : IRequestHandler<ListarWidgetsQuery, Result<IReadOnlyList<DashboardWidgetResponse>>>
{
    public async Task<Result<IReadOnlyList<DashboardWidgetResponse>>> Handle(
        ListarWidgetsQuery request, CancellationToken cancellationToken)
    {
        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure<IReadOnlyList<DashboardWidgetResponse>>(Error.Forbidden(
                "Dashboard.Widgets.ActorNoResuelto", "No se pudo resolver el actor autenticado."));
        }

        var specification = new WidgetsDeUsuarioSpecification(actorUserId.Value);

        var resultado = await repository.ListAsync(
            specification,
            w => new DashboardWidgetResponse(w.Id, w.Tipo, w.Titulo, w.WorkflowDefinitionId, w.Orden),
            cancellationToken);

        return Result.Success(resultado);
    }
}
