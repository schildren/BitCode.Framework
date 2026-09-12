using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.FeatureManagement.Flags;

/// <summary>
/// Evento de integración público del módulo Feature Management -- el hecho de negocio "un flag quedó
/// activo" cruza el límite de este bounded context: otros módulos consumidores del flag pueden
/// suscribirse para invalidar una copia cacheada del estado sin sondear el endpoint de lectura.
/// Registrado en <c>docs/catalogo-eventos.md</c> (regla dura 27). Implementa DELIBERADAMENTE tanto
/// <see cref="DomainEvent"/> (para que <c>OutboxSaveChangesInterceptor</c> lo recolecte de
/// <see cref="FeatureFlag"/>) como <see cref="IIntegrationEvent"/> (para que <c>OutboxBatchProcessor</c>
/// lo publique), mismo patrón que <c>CatalogoVersionPublicadaIntegrationEvent</c> (Catalogs and
/// Parameters, Fase 6 módulo 3).
/// </summary>
public sealed record FeatureFlagActivadoIntegrationEvent(Guid FeatureFlagId, string Nombre)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "FeatureManagement.FeatureFlagActivado";

    public int SchemaVersion => 1;

    /// <summary>Todos los eventos del mismo flag (activaciones/desactivaciones sucesivas) quedan en la
    /// misma partición -- un consumidor que necesite ver la historia de un flag en orden lo obtiene sin
    /// trabajo adicional.</summary>
    public string PartitionKey => FeatureFlagId.ToString();
}
