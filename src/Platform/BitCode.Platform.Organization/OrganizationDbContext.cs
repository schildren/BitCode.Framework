using BitCode.Framework.Platform.Organization.Areas;
using BitCode.Framework.Platform.Organization.Cargos;
using BitCode.Framework.Platform.Organization.Empresas;
using BitCode.Framework.Platform.Organization.Sucursales;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.Organization;

/// <summary>
/// Dueño exclusivo del esquema del módulo Organization (Fase 6, módulo 2 del Plan Maestro): tablas
/// <c>Empresas</c>, <c>Sucursales</c>, <c>Areas</c>, <c>Cargos</c>, más <c>IdempotencyKeys</c>/
/// <c>OutboxMessages</c>/<c>InboxMessages</c> (configuradas automáticamente por
/// <see cref="MultiTenantDbContext"/>). Ningún otro módulo de plataforma debe leer/escribir estas
/// tablas directamente -- la única superficie pública para consultarlas/mutarlas son los contratos de
/// este módulo (comandos/queries mapeados por
/// <c>OrganizationEndpointRouteBuilderExtensions</c>), ver <c>docs/guia-organization.md</c>.
/// </summary>
public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<Empresa> Empresas => Set<Empresa>();

    public DbSet<Sucursal> Sucursales => Set<Sucursal>();

    public DbSet<Area> Areas => Set<Area>();

    public DbSet<Cargo> Cargos => Set<Cargo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Empresa>(builder =>
        {
            builder.Property(e => e.RazonSocial).HasMaxLength(200).IsRequired();
            builder.Property(e => e.Identificador).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<Sucursal>(builder =>
        {
            builder.Property(s => s.Nombre).HasMaxLength(200).IsRequired();
            builder.Property(s => s.Direccion).HasMaxLength(400);
            builder.HasIndex(s => s.EmpresaId);
        });

        modelBuilder.Entity<Area>(builder =>
        {
            builder.Property(a => a.Nombre).HasMaxLength(200).IsRequired();
            builder.HasIndex(a => a.SucursalId);
        });

        modelBuilder.Entity<Cargo>(builder =>
        {
            builder.Property(c => c.Nombre).HasMaxLength(200).IsRequired();
            builder.HasIndex(c => c.AreaId);
        });
    }
}
