using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Catalogos;

// F1-21: expone page/pageSize crudos del cliente HTTP -- la validación de límites (nunca "todas las
// filas") ocurre dentro del handler, vía PageRequest.Create, antes de tocar el repositorio.
internal sealed record ListarCatalogosQuery(int Page, int PageSize) : IQuery<PagedResult<CatalogoResponse>>;

internal sealed class ListarCatalogosQueryHandler(IReadRepository<Catalogo, Guid> repository)
    : IRequestHandler<ListarCatalogosQuery, Result<PagedResult<CatalogoResponse>>>
{
    public async Task<Result<PagedResult<CatalogoResponse>>> Handle(
        ListarCatalogosQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<CatalogoResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodosLosCatalogosOrdenadosPorCodigoSpecification(),
            c => new CatalogoResponse(c.Id, c.Codigo, c.Nombre, c.Descripcion),
            pageRequestResult.Value,
            cancellationToken);
    }
}
