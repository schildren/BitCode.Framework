using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BitCode.Framework.Shared.Infrastructure.Security.Identity;

/// <summary>
/// Variante de MultiTenantDbContext (Fase 1) para proyectos consumidores que necesitan Identity:
/// no puede heredar de MultiTenantDbContext porque IdentityDbContext ya ocupa la base de herencia,
/// así que reutiliza la misma lógica de filtro global vía MultiTenancyModelConfigurator. Un
/// proyecto que no necesita autenticación sigue usando MultiTenantDbContext (Fase 1) sin esta
/// dependencia a Identity.
/// </summary>
public abstract class MultiTenantIdentityDbContext<TUser, TRole>
    : IdentityDbContext<TUser, TRole, Guid>
    where TUser : ApplicationUser
    where TRole : ApplicationRole
{
    private readonly Guid _tenantId;
    private readonly bool _isMultiTenancyEnabled;

    protected MultiTenantIdentityDbContext(DbContextOptions options, ITenantProvider tenantProvider) : base(options)
    {
        _tenantId = tenantProvider.TenantId ?? Guid.Empty;
        _isMultiTenancyEnabled = tenantProvider.IsMultiTenancyEnabled;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, PerInstanceModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        MultiTenancyModelConfigurator.ApplyGlobalFilters(modelBuilder, _tenantId, _isMultiTenancyEnabled);
    }
}
