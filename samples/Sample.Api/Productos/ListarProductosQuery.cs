using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Specifications;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace Sample.Api.Productos;

// F1-21: expone page/pageSize crudos del cliente HTTP — la validación de límites (nunca "todas las
// filas") ocurre dentro del handler, vía PageRequest.Create, antes de tocar el repositorio.
public record ListarProductosQuery(int Page, int PageSize) : IQuery<PagedResult<ProductoResponse>>;

public class TodosLosProductosOrdenadosPorNombreSpecification : Specification<Producto>
{
    public TodosLosProductosOrdenadosPorNombreSpecification() => ApplyOrderBy(p => p.Nombre);
}

public class ListarProductosQueryHandler(IReadRepository<Producto, Guid> repository)
    : IRequestHandler<ListarProductosQuery, Result<PagedResult<ProductoResponse>>>
{
    public async Task<Result<PagedResult<ProductoResponse>>> Handle(
        ListarProductosQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<ProductoResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodosLosProductosOrdenadosPorNombreSpecification(),
            p => new ProductoResponse(p.Id, p.Nombre, p.Precio),
            pageRequestResult.Value,
            cancellationToken);
    }
}
