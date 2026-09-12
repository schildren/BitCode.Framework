using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Specifications;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Cargos;

internal sealed record ListarCargosQuery(Guid AreaId) : IQuery<IReadOnlyList<CargoResponse>>;

internal sealed class CargosDeAreaOrdenadosPorNombreSpecification : Specification<Cargo>
{
    public CargosDeAreaOrdenadosPorNombreSpecification(Guid areaId) : base(c => c.AreaId == areaId) =>
        ApplyOrderBy(c => c.Nombre);
}

internal sealed class ListarCargosQueryHandler(IReadRepository<Cargo, Guid> repository)
    : IRequestHandler<ListarCargosQuery, Result<IReadOnlyList<CargoResponse>>>
{
    public async Task<Result<IReadOnlyList<CargoResponse>>> Handle(ListarCargosQuery request, CancellationToken cancellationToken)
    {
        var cargos = await repository.ListAsync(
            new CargosDeAreaOrdenadosPorNombreSpecification(request.AreaId),
            c => new CargoResponse(c.Id, c.AreaId, c.Nombre),
            cancellationToken);

        return Result.Success(cargos);
    }
}
