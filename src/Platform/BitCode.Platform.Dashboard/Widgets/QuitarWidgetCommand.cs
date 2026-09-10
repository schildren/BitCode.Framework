using BitCode.Framework.Platform.Dashboard.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Dashboard.Widgets;

internal sealed record QuitarWidgetCommand(Guid Id) : ICommand;

internal sealed class QuitarWidgetCommandValidator : AbstractValidator<QuitarWidgetCommand>
{
    public QuitarWidgetCommandValidator() => RuleFor(c => c.Id).NotEmpty();
}

internal sealed class QuitarWidgetCommandHandler(
    IRepository<DashboardWidget, Guid> repository, IDashboardActorContext actorContext)
    : IRequestHandler<QuitarWidgetCommand, Result>
{
    public async Task<Result> Handle(QuitarWidgetCommand request, CancellationToken cancellationToken)
    {
        var widget = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (widget is null)
        {
            return Result.Failure(Error.NotFound(
                "Dashboard.Widgets.NoEncontrado", $"No existe el widget {request.Id}."));
        }

        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure(Error.Forbidden(
                "Dashboard.Widgets.ActorNoResuelto", "No se pudo resolver el actor autenticado."));
        }

        // Ownership: solo el dueño del widget puede quitarlo de su propio dashboard -- sin este chequeo,
        // cualquier actor con el permiso genérico (dashboard.widgets.administrar) podía borrar el widget
        // de OTRO usuario por id (mismo tipo de hallazgo IDOR que ya corrigió Task Inbox, Fase 6, módulo
        // 7).
        if (widget.UserId != actorUserId.Value)
        {
            return Result.Failure(Error.Forbidden(
                "Dashboard.Widgets.NoAutorizado", "Solo el dueño del widget puede quitarlo de su dashboard."));
        }

        repository.Remove(widget);
        return Result.Success();
    }
}
