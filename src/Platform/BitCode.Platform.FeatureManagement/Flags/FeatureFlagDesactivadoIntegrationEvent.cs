using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.FeatureManagement.Flags;

/// <summary>Simétrico de <see cref="FeatureFlagActivadoIntegrationEvent"/> -- ver esa clase para el
/// razonamiento completo. Registrado en <c>docs/catalogo-eventos.md</c>.</summary>
public sealed record FeatureFlagDesactivadoIntegrationEvent(Guid FeatureFlagId, string Nombre)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "FeatureManagement.FeatureFlagDesactivado";

    public int SchemaVersion => 1;

    public string PartitionKey => FeatureFlagId.ToString();
}
