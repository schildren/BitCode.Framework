using BitCode.Framework.Shared.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class RepositoryBaseTests
{
    private static TestDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public async Task AddAsync_ThenSaveChanges_PersistsEntity()
    {
        using var context = CreateContext(nameof(AddAsync_ThenSaveChanges_PersistsEntity));
        var repository = new RepositoryBase<TestEntity, Guid>(context);
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);

        await repository.AddAsync(entity);
        await context.SaveChangesAsync();

        var found = await repository.GetByIdAsync(entity.Id);
        found.Should().NotBeNull();
        found!.Name.Should().Be("Alpha");
    }

    [Fact]
    public async Task Update_ThenSaveChanges_PersistsChanges()
    {
        using var context = CreateContext(nameof(Update_ThenSaveChanges_PersistsChanges));
        var repository = new RepositoryBase<TestEntity, Guid>(context);
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);
        await repository.AddAsync(entity);
        await context.SaveChangesAsync();

        entity.Amount = 99;
        repository.Update(entity);
        await context.SaveChangesAsync();

        var found = await repository.GetByIdAsync(entity.Id);
        found!.Amount.Should().Be(99);
    }

    [Fact]
    public async Task Remove_ThenSaveChanges_DeletesEntity()
    {
        using var context = CreateContext(nameof(Remove_ThenSaveChanges_DeletesEntity));
        var repository = new RepositoryBase<TestEntity, Guid>(context);
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);
        await repository.AddAsync(entity);
        await context.SaveChangesAsync();

        repository.Remove(entity);
        await context.SaveChangesAsync();

        var found = await repository.GetByIdAsync(entity.Id);
        found.Should().BeNull();
    }

    [Fact]
    public async Task SpecializedRepository_CanExtendGenericContractWithCustomQuery()
    {
        using var context = CreateContext(nameof(SpecializedRepository_CanExtendGenericContractWithCustomQuery));
        var repository = new TestEntityRepository(context);
        await repository.AddAsync(new TestEntity(Guid.NewGuid(), "Alpha", 10));
        await context.SaveChangesAsync();

        var found = await repository.GetByNameAsync("Alpha");

        found.Should().NotBeNull();
        found!.Name.Should().Be("Alpha");
    }
}
