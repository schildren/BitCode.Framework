using BitCode.Framework.Shared.Infrastructure.Persistence.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class SoftDeleteInterceptorTests
{
    private static TestDbContext CreateContext(string dbName, string? userId)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new SoftDeleteInterceptor(new FakeCurrentUserProvider(userId)))
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_OnRemovedEntity_ConvertsToSoftDelete()
    {
        using var context = CreateContext(nameof(SaveChangesAsync_OnRemovedEntity_ConvertsToSoftDelete), "user-1");
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        context.TestEntities.Remove(entity);
        await context.SaveChangesAsync();

        entity.IsDeleted.Should().BeTrue();
        entity.DeletedAtUtc.Should().NotBeNull();
        entity.DeletedBy.Should().Be("user-1");

        var stillInStore = await context.TestEntities.FindAsync(entity.Id);
        stillInStore.Should().NotBeNull("el registro no debe eliminarse físicamente");
    }

    [Fact]
    public async Task ListAsync_AfterSoftDelete_StillReturnsEntityWithoutGlobalFilter()
    {
        using var context = CreateContext(nameof(ListAsync_AfterSoftDelete_StillReturnsEntityWithoutGlobalFilter), "user-1");
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();
        context.TestEntities.Remove(entity);
        await context.SaveChangesAsync();

        var all = await context.TestEntities.ToListAsync();

        all.Should().ContainSingle(e => e.Id == entity.Id && e.IsDeleted);
    }
}
