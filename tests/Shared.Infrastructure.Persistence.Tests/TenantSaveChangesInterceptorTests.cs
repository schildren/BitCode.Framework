using BitCode.Framework.Shared.Infrastructure.Persistence.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class TenantSaveChangesInterceptorTests
{
    private static TestDbContext CreateContext(string dbName, FakeTenantProvider tenantProvider)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new TenantSaveChangesInterceptor(tenantProvider))
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_WithMultiTenancyEnabled_AssignsCurrentTenantId()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(
            nameof(SaveChangesAsync_WithMultiTenancyEnabled_AssignsCurrentTenantId),
            new FakeTenantProvider(tenantId));
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);

        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        entity.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task SaveChangesAsync_WithMultiTenancyDisabled_DoesNotAssignTenantId()
    {
        using var context = CreateContext(
            nameof(SaveChangesAsync_WithMultiTenancyDisabled_DoesNotAssignTenantId),
            new FakeTenantProvider(null, isMultiTenancyEnabled: false));
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);

        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        entity.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTenantIdAlreadySet_DoesNotOverrideIt()
    {
        var explicitTenantId = Guid.NewGuid();
        var currentTenantId = Guid.NewGuid();
        using var context = CreateContext(
            nameof(SaveChangesAsync_WhenTenantIdAlreadySet_DoesNotOverrideIt),
            new FakeTenantProvider(currentTenantId));
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10) { TenantId = explicitTenantId };

        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        entity.TenantId.Should().Be(explicitTenantId);
    }
}
