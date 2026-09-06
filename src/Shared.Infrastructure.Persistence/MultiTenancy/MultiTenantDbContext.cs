using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.Concurrency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;

public abstract class MultiTenantDbContext : DbContext
{
    private readonly Guid _tenantId;
    private readonly bool _isMultiTenancyEnabled;

    protected MultiTenantDbContext(DbContextOptions options, ITenantProvider tenantProvider) : base(options)
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
        ConcurrencyModelConfigurator.ApplyConcurrencyTokens(modelBuilder);
        TenantIndexModelConfigurator.ApplyTenantIndexes(modelBuilder);
    }
}
