using BitCode.Framework.Shared.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class UnitOfWorkTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _context;

    public UnitOfWorkTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TestDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutExplicitTransaction_PersistsData()
    {
        var repository = new RepositoryBase<TestEntity, Guid>(_context);
        var unitOfWork = new UnitOfWork(_context);
        var entity = new TestEntity(Guid.NewGuid(), "Alpha", 10);

        await repository.AddAsync(entity);
        await unitOfWork.SaveChangesAsync();

        var found = await repository.GetByIdAsync(entity.Id);
        found.Should().NotBeNull();
    }

    [Fact]
    public async Task CommitAsync_PersistsChangesMadeDuringTransaction()
    {
        var repository = new RepositoryBase<TestEntity, Guid>(_context);
        var unitOfWork = new UnitOfWork(_context);
        var entity = new TestEntity(Guid.NewGuid(), "Beta", 20);

        await unitOfWork.BeginTransactionAsync();
        await repository.AddAsync(entity);
        await unitOfWork.CommitAsync();

        var found = await repository.GetByIdAsync(entity.Id);
        found.Should().NotBeNull();
    }

    [Fact]
    public async Task RollbackAsync_DiscardsChangesMadeDuringTransaction()
    {
        var repository = new RepositoryBase<TestEntity, Guid>(_context);
        var unitOfWork = new UnitOfWork(_context);
        var entity = new TestEntity(Guid.NewGuid(), "Gamma", 30);

        await unitOfWork.BeginTransactionAsync();
        await repository.AddAsync(entity);
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.RollbackAsync();

        using var verificationContext = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options);
        var found = await verificationContext.TestEntities.FindAsync(entity.Id);
        found.Should().BeNull();
    }

    [Fact]
    public async Task CommitAsync_WithoutActiveTransaction_Throws()
    {
        var unitOfWork = new UnitOfWork(_context);

        var act = async () => await unitOfWork.CommitAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
