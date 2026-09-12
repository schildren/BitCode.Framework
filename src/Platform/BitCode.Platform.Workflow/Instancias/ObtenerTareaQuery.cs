using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Instancias;

internal sealed record ObtenerTareaQuery(Guid Id) : IQuery<WorkflowTaskResponse>;

internal sealed class ObtenerTareaQueryHandler(IReadRepository<WorkflowTask, Guid> repository)
    : IRequestHandler<ObtenerTareaQuery, Result<WorkflowTaskResponse>>
{
    public async Task<Result<WorkflowTaskResponse>> Handle(ObtenerTareaQuery request, CancellationToken cancellationToken)
    {
        var tarea = await repository.GetByIdAsync(request.Id, cancellationToken);

        return tarea is null
            ? Result.Failure<WorkflowTaskResponse>(
                Error.NotFound("Workflow.Tareas.NoEncontrada", $"No existe la tarea {request.Id}."))
            : new WorkflowTaskResponse(
                tarea.Id, tarea.WorkflowInstanceId, tarea.WorkflowStateId, tarea.Titulo, tarea.AsignadoAUserId,
                tarea.Estado, tarea.AccionResuelta, tarea.SlaVencimientoUtc, tarea.Escalada);
    }
}
