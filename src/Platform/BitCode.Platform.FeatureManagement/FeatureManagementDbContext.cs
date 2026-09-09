using BitCode.Framework.Platform.FeatureManagement.Flags;
using BitCode.Framework.Platform.FeatureManagement.Rollouts;
using BitCode.Framework.Platform.FeatureManagement.Segmentos;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.FeatureManagement;

/// <summary>
/// Dueño exclusivo del esquema del módulo Feature Management (Fase 6, módulo 4 del Plan Maestro): tablas
/// <c>FeatureFlags</c>, <c>Segmentos</c>, <c>Rollouts</c>, más
/// <c>IdempotencyKeys</c>/<c>OutboxMessages</c>/<c>InboxMessages</c> (configuradas automáticamente por
/// <see cref="MultiTenantDbContext"/>). Ningún otro módulo de plataforma debe leer/escribir estas tablas
/// directamente -- la única superficie pública para consultarlas/mutarlas son los contratos de este
/// módulo (comandos/queries mapeados por <c>FeatureManagementEndpointRouteBuilderExtensions</c>), ver
/// <c>docs/guia-feature-management.md</c>.
/// </summary>
public sealed class FeatureManagementDbContext(DbContextOptions<FeatureManagementDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    public DbSet<Segmento> Segmentos => Set<Segmento>();

    public DbSet<Rollout> Rollouts => Set<Rollout>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FeatureFlag>(builder =>
        {
            builder.Property(f => f.Nombre).HasMaxLength(128).IsRequired();
            builder.Property(f => f.Descripcion).HasMaxLength(500);
            // Único por tenant -- dos flags del mismo tenant no pueden compartir nombre lógico, pero dos
            // tenants distintos sí pueden tener, cada uno, un flag "nuevo-checkout" propio. Guardrail de
            // datos contra la condición de carrera de CrearFeatureFlagCommandHandler (ver ese archivo).
            builder.HasIndex(f => new { f.TenantId, f.Nombre }).IsUnique();
        });

        modelBuilder.Entity<Segmento>(builder =>
        {
            builder.Property(s => s.Nombre).HasMaxLength(128).IsRequired();
            builder.HasIndex(s => new { s.TenantId, s.Nombre }).IsUnique();
        });

        modelBuilder.Entity<Rollout>(builder =>
        {
            builder.HasIndex(r => r.FeatureFlagId);
            // Guardrail de datos contra la condición de carrera de CrearRolloutCommandHandler (ver ese
            // archivo): evita persistir dos veces la misma asociación FeatureFlag-Segmento.
            builder.HasIndex(r => new { r.FeatureFlagId, r.SegmentoId }).IsUnique();
        });
    }
}
