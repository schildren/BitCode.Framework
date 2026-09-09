using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Specifications;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Sucursales;

internal sealed record ListarSucursalesQuery(Guid EmpresaId, int Page, int PageSize)
    : IQuery<PagedResult<SucursalResponse>>;

internal sealed class SucursalesDeEmpresaOrdenadasPorNombreSpecification : Specification<Sucursal>
{
    public SucursalesDeEmpresaOrdenadasPorNombreSpecification(Guid empresaId) : base(s => s.EmpresaId == empresaId) =>
        ApplyOrderBy(s => s.Nombre);
}

internal sealed class ListarSucursalesQueryHandler(IReadRepository<Sucursal, Guid> repository)
    : IRequestHandler<ListarSucursalesQuery, Result<PagedResult<SucursalResponse>>>
{
    public async Task<Result<PagedResult<SucursalResponse>>> Handle(
        ListarSucursalesQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<SucursalResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new SucursalesDeEmpresaOrdenadasPorNombreSpecification(request.EmpresaId),
            s => new SucursalResponse(s.Id, s.EmpresaId, s.Nombre, s.Direccion, s.Activa),
            pageRequestResult.Value,
            cancellationToken);
    }
}
