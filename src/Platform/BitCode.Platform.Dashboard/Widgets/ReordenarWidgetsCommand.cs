using BitCode.Framework.Platform.Dashboard.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Dashboard.Widgets;

/// <summary>
/// Reordena TODOS los widgets del propio dashboard de una sola vez -- <see cref="WidgetIdsEnOrden"/> es la
/// lista completa de ids en el orden final deseado (posición 0 = primero). Un <c>ICommand</c> simple (no
/// <c>ITransactionalCommand</c>): aunque este handler modifica varias filas (<see cref="DashboardWidget"/>
/// distintos), todas se persisten en el ÚNICO <c>SaveChangesAsync</c> que <c>TransactionBehavior</c> ya
/// ejecuta al final del pipeline (regla dura 1/3, <c>docs/convenciones.md</c>) -- <c>ITransactionalCommand</c>
/// solo hace falta cuando el handler necesita el resultado YA PERSISTIDO de un paso intermedio antes de
/// ejecutar el siguiente, que no es el caso acá.
/// </summary>
internal sealed record ReordenarWidgetsCommand(IReadOnlyList<Guid> WidgetIdsEnOrden) : ICommand;

internal sealed class ReordenarWidgetsCommandValidator : AbstractValidator<ReordenarWidgetsCommand>
{
    public ReordenarWidgetsCommandValidator()
    {
        RuleFor(c => c.WidgetIdsEnOrden).NotEmpty();
        RuleFor(c => c.WidgetIdsEnOrden)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("WidgetIdsEnOrden no puede tener ids repetidos.");
    }
}

internal sealed class ReordenarWidgetsCommandHandler(
    IRepository<DashboardWidget, Guid> repository,
    IReadRepository<DashboardWidget, Guid> readRepository,
    IDashboardActorContext actorContext)
    : IRequestHandler<ReordenarWidgetsCommand, Result>
{
    public async Task<Result> Handle(ReordenarWidgetsCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure(Error.Forbidden(
                "Dashboard.Widgets.ActorNoResuelto", "No se pudo resolver el actor autenticado."));
        }

        var widgetsDelUsuario = await readRepository.ListAsync(
            new WidgetsDeUsuarioSpecification(actorUserId.Value), cancellationToken);
        var widgetsPorId = widgetsDelUsuario.ToDictionary(w => w.Id);

        // Ownership + integridad del conjunto: el nuevo orden debe cubrir EXACTAMENTE los widgets que ya
        // pertenecen al actor -- ni un id de otro usuario (IDOR), ni un subconjunto parcial (dejaría
        // Orden inconsistente entre los widgets reordenados y los que quedaron afuera del pedido).
        if (request.WidgetIdsEnOrden.Count != widgetsPorId.Count ||
            request.WidgetIdsEnOrden.Any(id => !widgetsPorId.ContainsKey(id)))
        {
            return Result.Failure(Error.Forbidden(
                "Dashboard.Widgets.NoAutorizado",
                "WidgetIdsEnOrden debe contener exactamente los widgets del propio dashboard, sin ids de otro usuario ni omisiones."));
        }

        for (var posicion = 0; posicion < request.WidgetIdsEnOrden.Count; posicion++)
        {
            var widget = widgetsPorId[request.WidgetIdsEnOrden[posicion]];
            widget.CambiarOrden(posicion);
            repository.Update(widget);
        }

        return Result.Success();
    }
}
