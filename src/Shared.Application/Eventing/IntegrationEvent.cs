namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// Base opcional de conveniencia para un <see cref="IIntegrationEvent"/> concreto, con el mismo
/// espíritu que <c>DomainEvent</c> (Shared.Kernel, F1-23): resuelve <see cref="EventId"/> y
/// <see cref="OccurredOnUtc"/> con un valor por defecto razonable en el momento de construcción, para
/// que un evento concreto solo tenga que declarar su <see cref="EventType"/> (y, si corresponde,
/// sobrescribir <see cref="SchemaVersion"/>) y sus propios datos de negocio.
/// </summary>
/// <remarks>
/// No es obligatorio heredar de este tipo — implementar <see cref="IIntegrationEvent"/> directamente
/// es igual de válido (por ejemplo, para un evento reconstruido por deserialización desde el payload
/// de un broker, donde <see cref="EventId"/>/<see cref="OccurredOnUtc"/> deben tomar el valor
/// original del productor en vez de uno nuevo).
/// </remarks>
public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    public abstract string EventType { get; }

    /// <summary>Versión inicial de esquema por defecto (1). Un evento que introduce un cambio incompatible de forma la sobrescribe.</summary>
    public virtual int SchemaVersion => 1;
}
