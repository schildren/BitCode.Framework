using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class MultiTenantTestDbContext(DbContextOptions<MultiTenantTestDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<TestEntity> TestEntities => Set<TestEntity>();
}
