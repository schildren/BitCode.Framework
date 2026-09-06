using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class MultiTenantTestDbContext(DbContextOptions<MultiTenantTestDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<TestEntity> TestEntities => Set<TestEntity>();

    public DbSet<ConcurrentTestEntity> ConcurrentTestEntities => Set<ConcurrentTestEntity>();

    // F1-23 (Outbox base): agregado de prueba dedicado a OutboxIntegrationTests, con eventos de
    // dominio reales (AggregateRoot<TId>) para ejercitar OutboxSaveChangesInterceptor.
    public DbSet<Integration.TestAccount> TestAccounts => Set<Integration.TestAccount>();
}
