using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

internal sealed record ObtenerWorkflowDefinitionQuery(Guid Id) : IQuery<WorkflowDefinitionResponse>;

internal sealed class ObtenerWorkflowDefinitionQueryHandler(IReadRepository<WorkflowDefinition, Guid> repository)
    : IRequestHandler<ObtenerWorkflowDefinitionQuery, Result<WorkflowDefinitionResponse>>
{
    public async Task<Result<WorkflowDefinitionResponse>> Handle(ObtenerWorkflowDefinitionQuery request, CancellationToken cancellationToken)
    {
        var definicion = await repository.GetByIdAsync(request.Id, cancellationToken);

        return definicion is null
            ? Result.Failure<WorkflowDefinitionResponse>(
                Error.NotFound("Workflow.Definiciones.NoEncontrada", $"No existe el workflow {request.Id}."))
            : new WorkflowDefinitionResponse(definicion.Id, definicion.Codigo, definicion.Nombre, definicion.Descripcion);
    }
}
