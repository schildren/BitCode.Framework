using BitCode.Framework.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Concurrency;

/// <summary>
/// Detecta por reflexión las entidades que implementan <see cref="IHasConcurrencyToken"/> (mismo
/// patrón que <c>MultiTenancyModelConfigurator</c> para <c>ISoftDelete</c>/<c>ITenantEntity</c>) y
/// configura su propiedad <c>RowVersion</c> como token de concurrencia de EF Core
/// (<c>IsRowVersion()</c>), sin requerir configuración adicional por entidad en
/// <c>OnModelCreating</c> (F1-08).
/// </summary>
public static class ConcurrencyModelConfigurator
{
    public static void ApplyConcurrencyTokens(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IHasConcurrencyToken).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(IHasConcurrencyToken.RowVersion))
                .IsRowVersion();
        }
    }
}
