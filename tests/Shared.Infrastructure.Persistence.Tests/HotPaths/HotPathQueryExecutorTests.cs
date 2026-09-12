using BitCode.Framework.Shared.Infrastructure.Persistence.HotPaths;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.HotPaths;

public class HotPathQueryExecutorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _context;

    public HotPathQueryExecutorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TestDbContext(options);
        _context.Database.EnsureCreated();

        _context.TestEntities.AddRange(
            new TestEntity(Guid.NewGuid(), "Alpha", 10),
            new TestEntity(Guid.NewGuid(), "Beta", 30),
            new TestEntity(Guid.NewGuid(), "Gamma", 50));
        _context.SaveChanges();
    }

    [Fact]
    public async Task SingleOrDefaultAsync_WithJustifiedQuery_ExecutesAndMaterializesResult()
    {
        var executor = new HotPathQueryExecutor(_context);

        var result = await executor.SingleOrDefaultAsync(new ResumenAmountHotPathQuery(threshold: 20));

        result.Should().NotBeNull();
        result!.Total.Should().Be(2);
        result.SumaAmount.Should().Be(80);
    }

    [Fact]
    public async Task SingleOrDefaultAsync_WithoutHotPathAttribute_Throws()
    {
        var executor = new HotPathQueryExecutor(_context);

        var act = async () => await executor.SingleOrDefaultAsync(new MissingAttributeHotPathQuery());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*no declara*HotPath*");
    }

    [Fact]
    public async Task SingleOrDefaultAsync_WithEmptyJustificationOrBenchmarkRef_Throws()
    {
        var executor = new HotPathQueryExecutor(_context);

        var act = async () => await executor.SingleOrDefaultAsync(new EmptyAttributeHotPathQuery());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Justification*BenchmarkRef*vacíos*");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
