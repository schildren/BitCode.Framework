using BitCode.Framework.Platform.Dashboard.Widgets;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.Dashboard;

/// <summary>
/// Dueño exclusivo del esquema del módulo Dashboard (Fase 6, módulo 12 del Plan Maestro -- el ÚLTIMO):
/// una única tabla propia, <c>DashboardWidgets</c> (preferencias de dashboard por usuario, ver
/// <see cref="DashboardWidget"/>), más <c>IdempotencyKeys</c>/<c>OutboxMessages</c>/<c>InboxMessages</c>
/// configuradas automáticamente por <see cref="MultiTenantDbContext"/> -- ninguna de las tres se usa
/// realmente en este módulo hoy (Dashboard no publica ni consume ningún evento de integración propio, ver
/// <c>docs/guia-dashboard.md</c>), pero <see cref="MultiTenantDbContext"/> las configura sin costo
/// adicional para cualquier consumidor futuro. Ningún otro módulo debe leer/escribir esta tabla
/// directamente -- la única superficie pública son los contratos de este módulo
/// (<c>DashboardEndpointRouteBuilderExtensions</c>).
/// </summary>
public sealed class DashboardDbContext(DbContextOptions<DashboardDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<DashboardWidget> DashboardWidgets => Set<DashboardWidget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DashboardWidget>(builder =>
        {
            // Consultado por CADA lectura/mutación de este módulo (WidgetsDeUsuarioSpecification): "mi
            // dashboard" siempre filtra por el usuario propietario, ordenado por posición -- sin este
            // índice compuesto, cada consulta sería un table scan completo de la tabla a medida que
            // crece con la actividad de todos los usuarios/tenants (regla dura 6, docs/convenciones.md).
            // Compuesto con TenantId aunque el filtro global de EF Core ya lo aplique -- mismo criterio
            // que corrigió el hallazgo Crítico de Integration Hub (Fase 6, módulo 9): un índice que NO
            // arranca por TenantId no ayuda al filtro global a elegir el camino de acceso más selectivo
            // en una tabla con muchos tenants.
            builder.HasIndex(w => new { w.TenantId, w.UserId, w.Orden });
        });
    }
}
