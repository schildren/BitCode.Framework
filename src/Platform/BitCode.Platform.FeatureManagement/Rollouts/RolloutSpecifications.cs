using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.FeatureManagement.Rollouts;

internal sealed class RolloutPorFlagYSegmentoSpecification : Specification<Rollout>
{
    public RolloutPorFlagYSegmentoSpecification(Guid featureFlagId, Guid segmentoId) =>
        ApplyCriteria(r => r.FeatureFlagId == featureFlagId && r.SegmentoId == segmentoId);
}

internal sealed class RolloutsDeFlagSpecification : Specification<Rollout>
{
    public RolloutsDeFlagSpecification(Guid featureFlagId) => ApplyCriteria(r => r.FeatureFlagId == featureFlagId);
}
