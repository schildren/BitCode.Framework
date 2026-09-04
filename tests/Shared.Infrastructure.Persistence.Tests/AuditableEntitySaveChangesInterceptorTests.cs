using BitCode.Framework.Shared.Infrastructure.Persistence.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class AuditableEntitySaveChangesInterceptorTests
{
    private static TestDbContext CreateContext(string dbName, string? userId)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new AuditableEntitySaveChangesInterceptor(new FakeCurrentUserProvider(userId)))
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_OnAddedEntity_PopulatesCreatedFields()
    {
        using var context = CreateContext(nameof(SaveChangesAsync_OnAddedEntity_PopulatesCreatedFields), "user-1");
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);

        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        entity.CreatedAtUtc.Should().NotBe(default);
        entity.CreatedBy.Should().Be("user-1");
        entity.ModifiedAtUtc.Should().BeNull();
        entity.ModifiedBy.Should().BeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_OnModifiedEntity_PopulatesModifiedFieldsOnly()
    {
        using var context = CreateContext(nameof(SaveChangesAsync_OnModifiedEntity_PopulatesModifiedFieldsOnly), "user-1");
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();
        var createdAt = entity.CreatedAtUtc;

        entity.Amount = 99;
        context.TestEntities.Update(entity);
        await context.SaveChangesAsync();

        entity.CreatedAtUtc.Should().Be(createdAt);
        entity.ModifiedAtUtc.Should().NotBeNull();
        entity.ModifiedBy.Should().Be("user-1");
    }
}
