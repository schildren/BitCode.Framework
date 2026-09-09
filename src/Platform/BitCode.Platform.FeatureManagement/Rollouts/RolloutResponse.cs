namespace BitCode.Framework.Platform.FeatureManagement.Rollouts;

public sealed record RolloutResponse(Guid Id, Guid FeatureFlagId, Guid SegmentoId);
