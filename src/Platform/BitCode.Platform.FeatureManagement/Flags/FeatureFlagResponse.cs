namespace BitCode.Framework.Platform.FeatureManagement.Flags;

public sealed record FeatureFlagResponse(Guid Id, string Nombre, string? Descripcion, bool Activo);
