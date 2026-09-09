using BitCode.Framework.Platform.TaskInbox.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.TaskInbox.Bandeja;

/// <summary>
/// Única mutación que Task Inbox posee de punta a punta sin depender de Workflow: marcar un ítem de
/// la propia bandeja como leído. No auditado vía <c>IAuditWriter</c> (F2-15) -- a diferencia de
/// resolver/delegar una tarea (una decisión de negocio), "leído" es un detalle de experiencia de
/// bandeja sin valor de auditoría/cumplimiento (mismo criterio que otros módulos de Fase 6 aplican a
/// operaciones de solo UX, ver <c>docs/guia-workflow.md</c> para el contraste con lo que SÍ se
/// audita).
/// </summary>
internal sealed record MarcarComoLeidaCommand(Guid Id) : ICommand;

internal sealed class MarcarComoLeidaCommandValidator : AbstractValidator<MarcarComoLeidaCommand>
{
    public MarcarComoLeidaCommandValidator() => RuleFor(c => c.Id).NotEmpty();
}

internal sealed class MarcarComoLeidaCommandHandler(
    IRepository<TaskInboxItem, Guid> repository, ITaskInboxActorContext actorContext)
    : IRequestHandler<MarcarComoLeidaCommand, Result>
{
    public async Task<Result> Handle(MarcarComoLeidaCommand request, CancellationToken cancellationToken)
    {
        var item = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound(
                "TaskInbox.Bandeja.NoEncontrado", $"No existe el ítem de bandeja {request.Id}."));
        }

        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure(new Error(
                "TaskInbox.Bandeja.ActorNoResuelto", "No se pudo resolver el actor autenticado.", ErrorType.Forbidden));
        }

        var result = item.MarcarComoLeida(actorUserId.Value);
        if (result.IsFailure)
        {
            return result;
        }

        repository.Update(item);
        return Result.Success();
    }
}
