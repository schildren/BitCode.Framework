using BitCode.Framework.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;

/// <summary>
/// F1-20 — convención automática de índice por defecto para toda entidad <see cref="ITenantEntity"/>.
/// Mismo patrón de detección por reflexión que <c>MultiTenancyModelConfigurator</c> (filtro global) y
/// <c>ConcurrencyModelConfigurator</c> (RowVersion): no requiere configuración adicional por entidad en
/// <c>OnModelCreating</c> de un proyecto consumidor.
///
/// Justificación: <c>MultiTenancyModelConfigurator.ApplyGlobalFilters</c> agrega, a TODA entidad
/// <see cref="ITenantEntity"/>, un filtro global <c>WHERE TenantId = @tenantId</c> (más
/// <c>IsDeleted = 0</c> si además implementa <see cref="ISoftDelete"/>) que EF Core aplica a cada
/// consulta contra esa entidad, sin excepción. Sin un índice que arranque por <c>TenantId</c>, ese
/// filtro se resuelve con un table/clustered index scan completo, un costo que crece linealmente con
/// el volumen total de filas de la tabla (todos los tenants juntos), no con el volumen del tenant
/// consultado — el peor patrón de escalado posible para un framework multi-tenant. Este es el tipo de
/// convención "gratis" que el framework debe dar por defecto: agregar un índice no cambia ningún
/// comportamiento funcional (a diferencia de RowVersion o el filtro global), así que no hay
/// contrapartida funcional que evaluar caso por caso.
///
/// Índice compuesto <c>(TenantId, IsDeleted)</c> cuando la entidad también implementa
/// <see cref="ISoftDelete"/>: el filtro global combina ambos predicados con AND en ese caso (ver
/// <c>MultiTenancyModelConfigurator.BuildGlobalFilter</c>), así que un índice que cubra las dos
/// columnas en ese orden sirve exactamente al filtro real que EF Core ejecuta en cada consulta,
/// evitando un segundo paso de filtrado tras el seek por <c>TenantId</c> solo.
///
/// Ver evidencia real (plan de ejecución antes/después, SQL Server real vía Testcontainers) en
/// <c>docs/evidencia-indices-sql.md</c> y el test
/// <c>tests/Shared.Infrastructure.Persistence.Tests/Integration/TenantIndexIntegrationTests.cs</c>.
/// </summary>
public static class TenantIndexModelConfigurator
{
    public static void ApplyTenantIndexes(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (!typeof(ITenantEntity).IsAssignableFrom(clrType))
            {
                continue;
            }

            var entityBuilder = modelBuilder.Entity(clrType);

            if (typeof(ISoftDelete).IsAssignableFrom(clrType))
            {
                entityBuilder.HasIndex(nameof(ITenantEntity.TenantId), nameof(ISoftDelete.IsDeleted));
            }
            else
            {
                entityBuilder.HasIndex(nameof(ITenantEntity.TenantId));
            }
        }
    }
}
