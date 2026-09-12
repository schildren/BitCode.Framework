using BitCode.Framework.Shared.Domain.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Idempotency;

/// <summary>
/// Registra explícitamente <see cref="IdempotencyKey"/> en el modelo de EF Core (F1-22) ANTES de que
/// corran los configuradores reflexivos existentes (<c>MultiTenancyModelConfigurator</c>,
/// <c>ConcurrencyModelConfigurator</c>, <c>TenantIndexModelConfigurator</c>, mismo patrón ya
/// establecido en <c>MultiTenantDbContext.OnModelCreating</c>): esos configuradores solo procesan
/// tipos que <c>modelBuilder.Model.GetEntityTypes()</c> ya conoce en el momento en que se ejecutan, y
/// <see cref="IdempotencyKey"/> no se expone como <c>DbSet</c> en ningún <c>MultiTenantDbContext</c>
/// consumidor (no lo necesita: <c>EfIdempotencyStore</c> accede vía
/// <c>DbContext.Set&lt;IdempotencyKey&gt;()</c>). Llamar a este método primero es lo que hace que
/// <see cref="IdempotencyKey"/> (que implementa <c>ITenantEntity</c>) reciba automáticamente el
/// filtro global de tenant y el índice no único por <c>TenantId</c>, igual que cualquier entidad de
/// negocio del proyecto consumidor.
/// </summary>
public static class IdempotencyModelConfigurator
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyKey>(entity =>
        {
            entity.Property(record => record.Key).HasMaxLength(200).IsRequired();
            entity.Property(record => record.RequestHash).HasMaxLength(64).IsRequired();

            // Único por (TenantId, Key): dos tenants distintos pueden reutilizar la misma clave
            // literal sin colisionar (F1-22); dentro del mismo tenant, la clave es única mientras la
            // entrada no haya expirado — IdempotencyBehavior elimina la entrada vencida antes de
            // insertar una nueva con la misma clave.
            entity.HasIndex(record => new { record.TenantId, record.Key }).IsUnique();
        });
    }
}
