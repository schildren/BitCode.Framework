using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.FeatureManagement.Rollouts;

/// <summary>
/// Asocia un <see cref="Flags.FeatureFlag"/> con un <see cref="Segmentos.Segmento"/> (Fase 6, módulo
/// Feature Management): mientras la asociación exista, el flag se evalúa como activo para todo contexto
/// que pertenezca al segmento asociado (además de estar globalmente <see cref="Flags.FeatureFlag.Activo"/>
/// -- ver <c>EvaluarFeatureFlagQueryHandler</c>). Un flag puede tener más de un <see cref="Rollout"/>
/// (varios segmentos, semántica OR: pertenece a CUALQUIERA de ellos). Es un <see cref="AggregateRoot{TId}"/>
/// propio (no un simple registro de tabla intermedia) porque su creación levanta un evento de integración
/// real (<see cref="RolloutIniciadoIntegrationEvent"/>) que otros módulos pueden necesitar consumir.
/// </summary>
public sealed class Rollout : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity
{
    public Guid FeatureFlagId { get; private set; }

    public Guid SegmentoId { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public Rollout(Guid id, Guid featureFlagId, Guid segmentoId) : base(id)
    {
        FeatureFlagId = featureFlagId;
        SegmentoId = segmentoId;

        RaiseDomainEvent(new RolloutIniciadoIntegrationEvent(Id, FeatureFlagId, SegmentoId));
    }

    private Rollout()
    {
    }
}
