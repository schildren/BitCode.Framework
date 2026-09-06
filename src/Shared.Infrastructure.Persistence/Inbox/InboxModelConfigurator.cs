using BitCode.Framework.Shared.Domain.Inbox;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Inbox;

/// <summary>
/// Registra explícitamente <see cref="InboxMessage"/> en el modelo de EF Core (F1-24) ANTES de que
/// corran los configuradores reflexivos existentes (<c>MultiTenancyModelConfigurator</c>,
/// <c>ConcurrencyModelConfigurator</c>, <c>TenantIndexModelConfigurator</c>) — mismo motivo y mismo
/// orden que <c>IdempotencyModelConfigurator</c>/<c>OutboxModelConfigurator</c> (F1-22/F1-23):
/// <see cref="InboxMessage"/> no se expone como <c>DbSet</c> en ningún <c>MultiTenantDbContext</c>
/// consumidor (<c>EfInboxStore</c> accede vía <c>DbContext.Set&lt;InboxMessage&gt;()</c>), así que sin
/// este registro explícito no recibiría el filtro global de tenant ni el índice por <c>TenantId</c>.
/// </summary>
public static class InboxModelConfigurator
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.Property(message => message.MessageId).HasMaxLength(200).IsRequired();
            entity.Property(message => message.MessageType).HasMaxLength(500).IsRequired();
            entity.Property(message => message.PayloadJson).IsRequired();
            entity.Property(message => message.Error).HasMaxLength(2000);

            // Único por (TenantId, MessageId) — mismo criterio que IdempotencyModelConfigurator: es
            // el índice físico que garantiza la deduplicación (criterio de aceptación "duplicados
            // descartados") incluso ante dos intentos concurrentes de insertar el mismo mensaje nuevo,
            // más allá del chequeo en memoria que hace InboxMessageProcessor.
            entity.HasIndex(message => new { message.TenantId, message.MessageId }).IsUnique();
        });
    }
}
