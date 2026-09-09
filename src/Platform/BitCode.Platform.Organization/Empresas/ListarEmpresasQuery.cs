using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Specifications;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Empresas;

// F1-21: expone page/pageSize crudos del cliente HTTP -- la validación de límites (nunca "todas las
// filas") ocurre dentro del handler, vía PageRequest.Create, antes de tocar el repositorio.
internal sealed record ListarEmpresasQuery(int Page, int PageSize) : IQuery<PagedResult<EmpresaResponse>>;

internal sealed class TodasLasEmpresasOrdenadasPorRazonSocialSpecification : Specification<Empresa>
{
    public TodasLasEmpresasOrdenadasPorRazonSocialSpecification() => ApplyOrderBy(e => e.RazonSocial);
}

internal sealed class ListarEmpresasQueryHandler(IReadRepository<Empresa, Guid> repository)
    : IRequestHandler<ListarEmpresasQuery, Result<PagedResult<EmpresaResponse>>>
{
    public async Task<Result<PagedResult<EmpresaResponse>>> Handle(
        ListarEmpresasQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<EmpresaResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodasLasEmpresasOrdenadasPorRazonSocialSpecification(),
            e => new EmpresaResponse(e.Id, e.RazonSocial, e.Identificador, e.Activa),
            pageRequestResult.Value,
            cancellationToken);
    }
}
