using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.FeatureManagement.Flags;

// F1-21: expone page/pageSize crudos del cliente HTTP -- la validación de límites (nunca "todas las
// filas") ocurre dentro del handler, vía PageRequest.Create, antes de tocar el repositorio.
internal sealed record ListarFeatureFlagsQuery(int Page, int PageSize) : IQuery<PagedResult<FeatureFlagResponse>>;

internal sealed class ListarFeatureFlagsQueryHandler(IReadRepository<FeatureFlag, Guid> repository)
    : IRequestHandler<ListarFeatureFlagsQuery, Result<PagedResult<FeatureFlagResponse>>>
{
    public async Task<Result<PagedResult<FeatureFlagResponse>>> Handle(
        ListarFeatureFlagsQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<FeatureFlagResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodosLosFlagsOrdenadosPorNombreSpecification(),
            f => new FeatureFlagResponse(f.Id, f.Nombre, f.Descripcion, f.Activo),
            pageRequestResult.Value,
            cancellationToken);
    }
}
