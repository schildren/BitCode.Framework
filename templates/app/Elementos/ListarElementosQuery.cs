using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Specifications;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace AppName.Elementos;

// Expone page/pageSize crudos del cliente HTTP -- la validación de límites (nunca "todas las filas")
// ocurre dentro del handler, vía PageRequest.Create, antes de tocar el repositorio.
public record ListarElementosQuery(int Page, int PageSize) : IQuery<PagedResult<ElementoResponse>>;

public class TodosLosElementosOrdenadosPorNombreSpecification : Specification<Elemento>
{
    public TodosLosElementosOrdenadosPorNombreSpecification() => ApplyOrderBy(e => e.Nombre);
}

public class ListarElementosQueryHandler(IReadRepository<Elemento, Guid> repository)
    : IRequestHandler<ListarElementosQuery, Result<PagedResult<ElementoResponse>>>
{
    public async Task<Result<PagedResult<ElementoResponse>>> Handle(
        ListarElementosQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<ElementoResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodosLosElementosOrdenadosPorNombreSpecification(),
            e => new ElementoResponse(e.Id, e.Nombre),
            pageRequestResult.Value,
            cancellationToken);
    }
}
