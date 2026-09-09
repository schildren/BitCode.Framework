using BitCode.Framework.Platform.Reporting.WorkflowInstancias;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.Reporting;

/// <summary>
/// Dueño exclusivo del esquema del módulo Reporting (Fase 6, módulo 11 del Plan Maestro): una única tabla
/// propia en este primer corte, <c>ReporteWorkflowInstancias</c> (read-model de ejemplo de referencia de
/// instancias de Workflow, ver <see cref="WorkflowInstancias.ReporteWorkflowInstancia"/>), más
/// <c>IdempotencyKeys</c>/<c>OutboxMessages</c>/<c>InboxMessages</c> configuradas automáticamente por
/// <see cref="MultiTenantDbContext"/> -- <c>InboxMessages</c> es, acá, el mecanismo real de deduplicación de
/// los dos eventos de integración de Workflow que este módulo consume (F1-24/F3-04, ver
/// <c>docs/guia-inbox-consumer.md</c>). Ningún otro módulo debe leer/escribir esta tabla directamente -- la
/// única superficie pública son los contratos de este módulo (<c>ReportingEndpointRouteBuilderExtensions</c>),
/// ver <c>docs/guia-reporting.md</c>.
/// </summary>
/// <remarks>
/// Un módulo futuro que agregue su propio read-model de reporting (ver "Cómo agregar un read-model nuevo"
/// en <c>docs/guia-reporting.md</c>) agrega su propio <c>DbSet&lt;T&gt;</c> a esta clase (o, preferiblemente,
/// su propio <c>DbContext</c> si el ownership de datos debe quedar separado) -- este primer corte no asume
/// que todos los read-models de reporting futuros comparten necesariamente el mismo esquema físico.
/// </remarks>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<ReporteWorkflowInstancia> ReporteWorkflowInstancias => Set<ReporteWorkflowInstancia>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ReporteWorkflowInstancia>(builder =>
        {
            // Consultado por ListarPromedioDuracionPorDefinicionQuery (agrupa por definición) y por el
            // filtro más común de ListarReporteWorkflowInstanciasQuery/ExportarReporteWorkflowInstanciasQuery
            // -- índice compuesto (TenantId siempre primero, hallazgo Crítico real de Integration Hub, Fase
            // 6 módulo 9) que cubre ambos casos sin table scan.
            builder.HasIndex(r => new { r.TenantId, r.WorkflowDefinitionId, r.Estado });
            builder.HasIndex(r => new { r.TenantId, r.IniciadaAtUtc });
        });
    }
}
