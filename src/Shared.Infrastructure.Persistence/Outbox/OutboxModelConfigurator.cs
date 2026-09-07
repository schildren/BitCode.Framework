using BitCode.Framework.Shared.Domain.Outbox;
using BitCode.Framework.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;

/// <summary>
/// Registra explícitamente <see cref="OutboxMessage"/> en el modelo de EF Core (F1-23) ANTES de que
/// corran los configuradores reflexivos existentes (<c>MultiTenancyModelConfigurator</c>,
/// <c>ConcurrencyModelConfigurator</c>, <c>TenantIndexModelConfigurator</c>) — mismo motivo y mismo
/// orden que <c>IdempotencyModelConfigurator</c> (F1-22): <see cref="OutboxMessage"/> no se expone
/// como <c>DbSet</c> en ningún <c>MultiTenantDbContext</c> consumidor (<c>OutboxSaveChangesInterceptor</c>
/// accede vía <c>DbContext.Set&lt;OutboxMessage&gt;()</c>), así que sin este registro explícito los
/// configuradores reflexivos nunca lo verían y no recibiría el filtro global de tenant.
/// </summary>
public static class OutboxModelConfigurator
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        // EF Core intenta descubrir por convención BitCode.Framework.Shared.Kernel.DomainEvent como
        // tipo de entidad porque AggregateRoot<TId>.DomainEvents lo expone como colección pública. Un
        // DomainEvent nunca se persiste como tabla propia (solo su JSON serializado dentro de
        // OutboxMessage.PayloadJson) — sin este Ignore, ModelValidator falla al no poder asignarle una
        // clave primaria.
        modelBuilder.Ignore<DomainEvent>();

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.Property(message => message.EventType).HasMaxLength(500).IsRequired();
            entity.Property(message => message.PayloadJson).IsRequired();
            entity.Property(message => message.Error).HasMaxLength(2000);
            entity.Property(message => message.LockedBy).HasMaxLength(200);

            // Índice pensado para el relay de Fase 3: filtrar rápido las filas pendientes de publicar
            // (ProcessedAtUtc nulo) sin recorrer toda la tabla.
            entity.HasIndex(message => message.ProcessedAtUtc);

            // F3-03 (Outbox Publisher): índice compuesto para el claim por lotes —
            // OutboxBatchProcessor.ClaimBatchAsync filtra "ProcessedAtUtc IS NULL AND (LockedUntilUtc
            // IS NULL OR LockedUntilUtc < @now)" ordenando por OccurredAtUtc; sin este índice, cada
            // ciclo de sondeo de cada instancia del worker escanearía toda la tabla.
            entity.HasIndex(message => new { message.ProcessedAtUtc, message.LockedUntilUtc, message.OccurredAtUtc });
        });
    }
}
