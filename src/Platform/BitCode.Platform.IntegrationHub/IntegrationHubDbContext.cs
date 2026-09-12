using BitCode.Framework.Platform.IntegrationHub.Conectores;
using BitCode.Framework.Platform.IntegrationHub.Solicitudes;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.IntegrationHub;

/// <summary>
/// Dueño exclusivo del esquema del módulo Integration Hub (Fase 6, módulo 9 del Plan Maestro): cuatro
/// tablas propias (<c>IntegrationConnectors</c>, <c>IntegrationFieldMappings</c>,
/// <c>IntegrationRequests</c>, <c>IntegrationRequestLogs</c>), más
/// <c>IdempotencyKeys</c>/<c>OutboxMessages</c>/<c>InboxMessages</c> configuradas automáticamente por
/// <see cref="MultiTenantDbContext"/> -- <c>OutboxMessages</c> es el mecanismo real de publicación de los
/// dos eventos de integración propios de este módulo
/// (<see cref="SolicitudIntegracionEnviadaIntegrationEvent"/>/<see cref="SolicitudIntegracionFallidaIntegrationEvent"/>).
/// Ningún otro módulo debe leer/escribir esta tabla directamente -- la única superficie pública son los
/// contratos de este módulo, ver <c>docs/guia-integration-hub.md</c>.
/// </summary>
public sealed class IntegrationHubDbContext(DbContextOptions<IntegrationHubDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<IntegrationConnector> IntegrationConnectors => Set<IntegrationConnector>();

    public DbSet<IntegrationFieldMapping> IntegrationFieldMappings => Set<IntegrationFieldMapping>();

    public DbSet<IntegrationRequest> IntegrationRequests => Set<IntegrationRequest>();

    public DbSet<IntegrationRequestLog> IntegrationRequestLogs => Set<IntegrationRequestLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<IntegrationConnector>(builder =>
        {
            // Clave lógica de un conector, única POR TENANT (no global) -- consultada por
            // EnviarSolicitudIntegracionCommand en cada encolado (ver ConectorActivoPorCodigoSpecification),
            // y por CrearConectorCommand para rechazar duplicados (regla dura 6, docs/convenciones.md:
            // unicidad reforzada también a nivel de base de datos, no solo en el handler). Compuesto con
            // TenantId -- mismo criterio que todo `Codigo` único de Fase 6 (CatalogsDbContext,
            // WorkflowDbContext, FeatureManagementDbContext): sin TenantId en el índice, un Tenant B no
            // podía crear un conector con el mismo código que ya usaba un Tenant A, aunque el filtro
            // global de EF Core hiciera que ninguno viera el conector del otro -- corregido tras
            // auditoría de arquitectura (2026-09-09), antes de este commit.
            builder.HasIndex(c => new { c.TenantId, c.Codigo }).IsUnique();
        });

        modelBuilder.Entity<IntegrationFieldMapping>(builder =>
        {
            builder.HasIndex(m => m.ConnectorId);
        });

        modelBuilder.Entity<IntegrationRequest>(builder =>
        {
            // Consultada por IntegrationOutboundProcessorJob en cada disparo (IgnoreQueryFilters,
            // cross-tenant) -- sin este índice, cada ciclo del job sería un table scan completo de la
            // tabla, mismo criterio que el índice equivalente de NotificationsDbContext.
            builder.HasIndex(r => new { r.Estado, r.ProximoReintentoUtc });
            builder.HasIndex(r => r.ConnectorId);
        });

        modelBuilder.Entity<IntegrationRequestLog>(builder =>
        {
            builder.HasIndex(l => l.IntegrationRequestId);
        });
    }
}
