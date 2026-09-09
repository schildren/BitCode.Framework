using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Instancias;

internal sealed record ObtenerInstanciaQuery(Guid Id) : IQuery<WorkflowInstanceResponse>;

internal sealed class ObtenerInstanciaQueryHandler(IReadRepository<WorkflowInstance, Guid> repository)
    : IRequestHandler<ObtenerInstanciaQuery, Result<WorkflowInstanceResponse>>
{
    public async Task<Result<WorkflowInstanceResponse>> Handle(ObtenerInstanciaQuery request, CancellationToken cancellationToken)
    {
        var instancia = await repository.GetByIdAsync(request.Id, cancellationToken);

        return instancia is null
            ? Result.Failure<WorkflowInstanceResponse>(
                Error.NotFound("Workflow.Instancias.NoEncontrada", $"No existe la instancia {request.Id}."))
            : new WorkflowInstanceResponse(
                instancia.Id, instancia.WorkflowDefinitionId, instancia.WorkflowVersionId, instancia.EstadoActualId,
                instancia.Estado, instancia.ObtenerVariables(), instancia.FinalizadaAtUtc);
    }
}
