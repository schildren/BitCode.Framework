using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Specifications;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Areas;

/// <summary>Sin paginación (mismo criterio que <c>ListarRolesQuery</c> de Identity Administration):
/// el número de áreas por sucursal es acotado en la práctica, y este primer corte no expone todavía
/// una consulta de árbol recursivo -- devuelve la lista plana de la sucursal, el cliente arma el árbol
/// con <see cref="AreaResponse.ParentAreaId"/> si lo necesita (pendiente explícito, ver la guía).</summary>
internal sealed record ListarAreasQuery(Guid SucursalId) : IQuery<IReadOnlyList<AreaResponse>>;

internal sealed class AreasDeSucursalOrdenadasPorNombreSpecification : Specification<Area>
{
    public AreasDeSucursalOrdenadasPorNombreSpecification(Guid sucursalId) : base(a => a.SucursalId == sucursalId) =>
        ApplyOrderBy(a => a.Nombre);
}

internal sealed class ListarAreasQueryHandler(IReadRepository<Area, Guid> repository)
    : IRequestHandler<ListarAreasQuery, Result<IReadOnlyList<AreaResponse>>>
{
    public async Task<Result<IReadOnlyList<AreaResponse>>> Handle(ListarAreasQuery request, CancellationToken cancellationToken)
    {
        var areas = await repository.ListAsync(
            new AreasDeSucursalOrdenadasPorNombreSpecification(request.SucursalId),
            a => new AreaResponse(a.Id, a.SucursalId, a.Nombre, a.ParentAreaId),
            cancellationToken);

        return Result.Success(areas);
    }
}
