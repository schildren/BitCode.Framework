using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

// F1-21: expone page/pageSize crudos del cliente HTTP -- la validación de límites ocurre dentro del
// handler, vía PageRequest.Create, antes de tocar el repositorio.
internal sealed record ListarWorkflowDefinitionsQuery(int Page, int PageSize) : IQuery<PagedResult<WorkflowDefinitionResponse>>;

internal sealed class ListarWorkflowDefinitionsQueryHandler(IReadRepository<WorkflowDefinition, Guid> repository)
    : IRequestHandler<ListarWorkflowDefinitionsQuery, Result<PagedResult<WorkflowDefinitionResponse>>>
{
    public async Task<Result<PagedResult<WorkflowDefinitionResponse>>> Handle(
        ListarWorkflowDefinitionsQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<WorkflowDefinitionResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodasLasWorkflowDefinicionesOrdenadasPorCodigoSpecification(),
            d => new WorkflowDefinitionResponse(d.Id, d.Codigo, d.Nombre, d.Descripcion),
            pageRequestResult.Value,
            cancellationToken);
    }
}
