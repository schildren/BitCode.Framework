using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.FeatureManagement.Rollouts;

internal sealed record ListarRolloutsDeFlagQuery(Guid FeatureFlagId) : IQuery<IReadOnlyList<RolloutResponse>>;

internal sealed class ListarRolloutsDeFlagQueryHandler(IReadRepository<Rollout, Guid> repository)
    : IRequestHandler<ListarRolloutsDeFlagQuery, Result<IReadOnlyList<RolloutResponse>>>
{
    public async Task<Result<IReadOnlyList<RolloutResponse>>> Handle(
        ListarRolloutsDeFlagQuery request, CancellationToken cancellationToken)
    {
        var rollouts = await repository.ListAsync(
            new RolloutsDeFlagSpecification(request.FeatureFlagId),
            r => new RolloutResponse(r.Id, r.FeatureFlagId, r.SegmentoId),
            cancellationToken);

        return Result.Success(rollouts);
    }
}
