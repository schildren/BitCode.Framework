using System.Text.Json;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Outbox;
using BitCode.Framework.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writer del patrón Outbox (F1-23): antes de que <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>
/// confirme el cambio de negocio, recolecta los <c>DomainEvent</c> pendientes de cada agregado
/// trackeado (<see cref="IHasDomainEvents.DomainEvents"/>) y los agrega como <see cref="OutboxMessage"/>
/// al mismo <see cref="DbContext.ChangeTracker"/> — quedan incluidos en el MISMO <c>SaveChangesAsync</c>
/// que persiste el cambio de negocio, nunca en uno separado. Esto es lo que garantiza el criterio de
/// aceptación "evento no se pierde tras commit": si el commit tiene éxito, el evento ya está en la base
/// junto con el cambio de negocio (una sola escritura atómica); si la transacción hace rollback (por
/// ejemplo, <c>ITransactionalCommand</c> con un paso posterior que falla, F1-07/F1-09), ni el cambio de
/// negocio ni el evento quedan persistidos, porque ambos comparten el mismo <c>ChangeTracker</c> y la
/// misma transacción física de SQL Server.
/// </summary>
/// <remarks>
/// Solo escribe la fila en la tabla <c>OutboxMessages</c> con <see cref="OutboxMessage.ProcessedAtUtc"/>
/// en <see langword="null"/> — el relay/publisher que lea esas filas y las publique a un broker externo
/// (Kafka, ADR <c>docs/adr/0005-mensajeria-kafka.md</c>, todavía "Proposed") es trabajo de Fase 3, fuera
/// de alcance de F1-23.
/// </remarks>
public class OutboxSaveChangesInterceptor(ITenantProvider tenantProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        WriteOutboxMessages(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        WriteOutboxMessages(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void WriteOutboxMessages(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var tenantId = tenantProvider.IsMultiTenancyEnabled ? tenantProvider.TenantId ?? Guid.Empty : Guid.Empty;
        var occurredAtUtc = DateTime.UtcNow;

        // Snapshot deliberado con ToList(): agregar entidades a context.Set<OutboxMessage>() mientras
        // se enumera context.ChangeTracker.Entries<T>() invalidaría el enumerador subyacente.
        var aggregatesWithPendingEvents = context.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregatesWithPendingEvents)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var eventType = domainEvent.GetType();

                context.Set<OutboxMessage>().Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EventType = eventType.AssemblyQualifiedName!,
                    PayloadJson = JsonSerializer.Serialize(domainEvent, eventType),
                    OccurredAtUtc = occurredAtUtc,
                });
            }

            // Evita volver a escribir el mismo evento si este agregado participa de un segundo
            // SaveChangesAsync dentro de la misma transacción (ITransactionalCommand con varios pasos,
            // F1-07/F1-09): el evento ya quedó en el ChangeTracker como parte de este SaveChanges.
            aggregate.ClearDomainEvents();
        }
    }
}
