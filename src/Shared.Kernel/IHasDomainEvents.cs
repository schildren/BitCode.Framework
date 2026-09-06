namespace BitCode.Framework.Shared.Kernel;

/// <summary>
/// Marcador no genérico de <see cref="AggregateRoot{TId}"/> (F1-23, Outbox base): existe porque
/// <see cref="AggregateRoot{TId}"/> es genérico sobre <c>TId</c> y EF Core no puede enumerar
/// <c>ChangeTracker.Entries&lt;AggregateRoot&lt;Guid&gt;&gt;()</c> y
/// <c>ChangeTracker.Entries&lt;AggregateRoot&lt;int&gt;&gt;()</c> con una sola llamada — un
/// interceptor de <c>SaveChanges</c> (como <c>OutboxSaveChangesInterceptor</c>, Shared.Infrastructure.
/// Persistence) necesita un único tipo no genérico para recolectar los eventos de dominio pendientes
/// de cualquier agregado, sin importar el tipo de su identificador. Mismo criterio que
/// <c>IAuditedEntity</c>/<c>ITenantEntity</c>/<c>ISoftDelete</c>: un contrato mínimo pensado para que
/// los interceptores de persistencia lo consuman vía <c>ChangeTracker.Entries&lt;T&gt;()</c>.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<DomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
