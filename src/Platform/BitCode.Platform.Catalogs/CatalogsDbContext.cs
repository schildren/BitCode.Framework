using BitCode.Framework.Platform.Catalogs.Catalogos;
using BitCode.Framework.Platform.Catalogs.Parametros;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.Catalogs;

/// <summary>
/// Dueño exclusivo del esquema del módulo Catalogs and Parameters (Fase 6, módulo 3 del Plan Maestro):
/// tablas <c>Catalogos</c>, <c>CatalogoVersiones</c>, <c>CatalogoItems</c>, <c>Parametros</c>,
/// <c>ParametroVigencias</c>, más <c>IdempotencyKeys</c>/<c>OutboxMessages</c>/<c>InboxMessages</c>
/// (configuradas automáticamente por <see cref="MultiTenantDbContext"/>). Ningún otro módulo de
/// plataforma debe leer/escribir estas tablas directamente -- la única superficie pública para
/// consultarlas/mutarlas son los contratos de este módulo (comandos/queries mapeados por
/// <c>CatalogsEndpointRouteBuilderExtensions</c>), ver <c>docs/guia-catalogs.md</c>.
/// </summary>
public sealed class CatalogsDbContext(DbContextOptions<CatalogsDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<Catalogo> Catalogos => Set<Catalogo>();

    public DbSet<CatalogoVersion> CatalogoVersiones => Set<CatalogoVersion>();

    public DbSet<CatalogoItem> CatalogoItems => Set<CatalogoItem>();

    public DbSet<Parametro> Parametros => Set<Parametro>();

    public DbSet<ParametroVigencia> ParametroVigencias => Set<ParametroVigencia>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Catalogo>(builder =>
        {
            builder.Property(c => c.Codigo).HasMaxLength(64).IsRequired();
            builder.Property(c => c.Nombre).HasMaxLength(200).IsRequired();
            builder.Property(c => c.Descripcion).HasMaxLength(500);
            // Único por tenant -- dos catálogos del mismo tenant no pueden compartir código lógico,
            // pero dos tenants distintos sí pueden tener, cada uno, un catálogo "MONEDAS" propio.
            builder.HasIndex(c => new { c.TenantId, c.Codigo }).IsUnique();
        });

        modelBuilder.Entity<CatalogoVersion>(builder =>
        {
            builder.HasIndex(v => v.CatalogoId);
            builder.HasIndex(v => new { v.CatalogoId, v.Numero }).IsUnique();
        });

        modelBuilder.Entity<CatalogoItem>(builder =>
        {
            builder.Property(i => i.Codigo).HasMaxLength(64).IsRequired();
            builder.Property(i => i.Etiqueta).HasMaxLength(200).IsRequired();
            builder.Property(i => i.Valor).HasMaxLength(500);
            builder.HasIndex(i => i.CatalogoVersionId);
        });

        modelBuilder.Entity<Parametro>(builder =>
        {
            builder.Property(p => p.Codigo).HasMaxLength(64).IsRequired();
            builder.Property(p => p.Nombre).HasMaxLength(200).IsRequired();
            builder.Property(p => p.Descripcion).HasMaxLength(500);
            builder.HasIndex(p => new { p.TenantId, p.Codigo }).IsUnique();
        });

        modelBuilder.Entity<ParametroVigencia>(builder =>
        {
            builder.Property(v => v.Valor).HasMaxLength(500).IsRequired();
            builder.HasIndex(v => v.ParametroId);
        });
    }
}
