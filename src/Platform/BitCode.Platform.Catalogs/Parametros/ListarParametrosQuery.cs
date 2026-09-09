using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Parametros;

// F1-21: expone page/pageSize crudos del cliente HTTP -- la validación de límites ocurre dentro del
// handler, vía PageRequest.Create, antes de tocar el repositorio.
internal sealed record ListarParametrosQuery(int Page, int PageSize) : IQuery<PagedResult<ParametroResponse>>;

internal sealed class ListarParametrosQueryHandler(IReadRepository<Parametro, Guid> repository)
    : IRequestHandler<ListarParametrosQuery, Result<PagedResult<ParametroResponse>>>
{
    public async Task<Result<PagedResult<ParametroResponse>>> Handle(
        ListarParametrosQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<ParametroResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodosLosParametrosOrdenadosPorCodigoSpecification(),
            p => new ParametroResponse(p.Id, p.Codigo, p.Nombre, p.Descripcion),
            pageRequestResult.Value,
            cancellationToken);
    }
}
