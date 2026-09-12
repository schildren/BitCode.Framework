using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.FeatureManagement.Rollouts;

/// <summary>
/// Evento de integración público del módulo Feature Management -- el hecho de negocio "un flag empezó a
/// hacer rollout hacia un segmento nuevo" cruza el límite de este bounded context. Registrado en
/// <c>docs/catalogo-eventos.md</c> (regla dura 27). Implementa DELIBERADAMENTE tanto
/// <see cref="DomainEvent"/> como <see cref="IIntegrationEvent"/>, mismo patrón que
/// <see cref="Flags.FeatureFlagActivadoIntegrationEvent"/>.
/// </summary>
public sealed record RolloutIniciadoIntegrationEvent(Guid RolloutId, Guid FeatureFlagId, Guid SegmentoId)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "FeatureManagement.RolloutIniciado";

    public int SchemaVersion => 1;

    /// <summary>Todos los rollouts del mismo flag quedan en la misma partición.</summary>
    public string PartitionKey => FeatureFlagId.ToString();
}
