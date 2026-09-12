namespace BitCode.Framework.Shared.Kernel;

/// <remarks>
/// Implementa <see cref="IHasDomainEvents"/> (F1-23, Outbox base) para que
/// <c>OutboxSaveChangesInterceptor</c> (Shared.Infrastructure.Persistence) pueda recolectar, dentro del
/// mismo <c>SaveChangesAsync</c> que persiste el cambio de negocio de este agregado, cada
/// <see cref="DomainEvent"/> levantado por <see cref="RaiseDomainEvent"/> y escribirlo en la tabla
/// <c>OutboxMessages</c> antes del commit — así el evento nunca se pierde tras un commit exitoso, ni
/// queda huérfano si la transacción hace rollback (ambos comparten el mismo <c>ChangeTracker</c>).
/// </remarks>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : IEquatable<TId>
{
    private readonly List<DomainEvent> _domainEvents = [];

    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(TId id) : base(id)
    {
    }

    protected void RaiseDomainEvent(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
