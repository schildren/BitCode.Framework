using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class PersistenceServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddSharedPersistence_ResolvesRepositoryUnitOfWorkAndDefaultProviders()
    {
        var services = new ServiceCollection();
        services.AddSharedPersistence<MultiTenantTestDbContext>(
            "Server=(localdb)\\mssqllocaldb;Database=BitCodeFrameworkTests;Trusted_Connection=True;");

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();

        repository.Should().NotBeNull();
        unitOfWork.Should().NotBeNull();
        tenantProvider.Should().BeOfType<MultiTenancy.NullTenantProvider>();
    }

    [Fact]
    public async Task AddSharedPersistence_RespectsTenantProviderRegisteredBeforehand()
    {
        var services = new ServiceCollection();
        var tenantId = Guid.NewGuid();
        services.AddScoped<ITenantProvider>(_ => new FakeTenantProvider(tenantId));

        services.AddSharedPersistence<MultiTenantTestDbContext>(
            "Server=(localdb)\\mssqllocaldb;Database=BitCodeFrameworkTests;Trusted_Connection=True;");

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();

        tenantProvider.TenantId.Should().Be(tenantId);
    }
}
