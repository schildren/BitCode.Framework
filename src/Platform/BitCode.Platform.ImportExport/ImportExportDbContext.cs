using BitCode.Framework.Platform.ImportExport.Exportacion;
using BitCode.Framework.Platform.ImportExport.Importacion;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.ImportExport;

/// <summary>
/// Dueño exclusivo del esquema del módulo Import and Export (Fase 6, módulo 10 del Plan Maestro): cuatro
/// tablas propias (<c>ImportJobs</c>, <c>ImportJobErrors</c>, <c>ExportJobs</c>, <c>ExportJobErrors</c>),
/// más <c>IdempotencyKeys</c>/<c>OutboxMessages</c>/<c>InboxMessages</c> configuradas automáticamente por
/// <see cref="MultiTenantDbContext"/> -- <c>OutboxMessages</c> es el mecanismo real de publicación de los
/// cuatro eventos de integración propios de este módulo. Ningún otro módulo debe leer/escribir esta tabla
/// directamente -- la única superficie pública son los contratos de este módulo, ver
/// <c>docs/guia-import-export.md</c>.
/// </summary>
public sealed class ImportExportDbContext(DbContextOptions<ImportExportDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();

    public DbSet<ImportJobError> ImportJobErrors => Set<ImportJobError>();

    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();

    public DbSet<ExportJobError> ExportJobErrors => Set<ExportJobError>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ImportJob>(builder =>
        {
            // Consultado por ImportBatchProcessorJob en cada disparo (IgnoreQueryFilters, cross-tenant) --
            // sin este índice, cada ciclo del job sería un table scan completo, mismo criterio que el
            // índice equivalente de IntegrationHubDbContext sobre IntegrationRequest.Estado.
            builder.HasIndex(j => j.Estado);
        });

        modelBuilder.Entity<ImportJobError>(builder =>
        {
            builder.HasIndex(e => e.ImportJobId);
        });

        modelBuilder.Entity<ExportJob>(builder =>
        {
            builder.HasIndex(j => j.Estado);
        });

        modelBuilder.Entity<ExportJobError>(builder =>
        {
            builder.HasIndex(e => e.ExportJobId);
        });
    }
}
