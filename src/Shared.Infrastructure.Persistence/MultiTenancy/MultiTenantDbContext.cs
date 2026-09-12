using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.Concurrency;
using BitCode.Framework.Shared.Infrastructure.Persistence.Idempotency;
using BitCode.Framework.Shared.Infrastructure.Persistence.Inbox;
using BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;
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
        // F1-22: registra IdempotencyKey en el modelo ANTES de los configuradores reflexivos de
        // abajo, para que también reciba el filtro global de tenant y el índice por TenantId (ver
        // IdempotencyModelConfigurator).
        IdempotencyModelConfigurator.Configure(modelBuilder);
        // F1-23: mismo motivo y mismo orden que IdempotencyModelConfigurator arriba — OutboxMessage
        // tampoco se expone como DbSet en un MultiTenantDbContext consumidor.
        OutboxModelConfigurator.Configure(modelBuilder);
        // F1-24: mismo motivo y mismo orden que los dos anteriores — InboxMessage tampoco se expone
        // como DbSet en un MultiTenantDbContext consumidor.
        InboxModelConfigurator.Configure(modelBuilder);
        MultiTenancyModelConfigurator.ApplyGlobalFilters(modelBuilder, _tenantId, _isMultiTenancyEnabled);
        ConcurrencyModelConfigurator.ApplyConcurrencyTokens(modelBuilder);
        TenantIndexModelConfigurator.ApplyTenantIndexes(modelBuilder);
    }
}
