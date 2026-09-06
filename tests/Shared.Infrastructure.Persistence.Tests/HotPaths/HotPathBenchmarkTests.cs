using System.Diagnostics;
using BitCode.Framework.Shared.Infrastructure.Persistence.HotPaths;
using BitCode.Framework.Shared.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.HotPaths;

/// <summary>
/// Benchmark que justifica <see cref="ResumenAmountHotPathQuery"/> (F1-18, criterio de aceptación
/// "Benchmark justifica cada bypass"). Compara, sobre el mismo dataset y la misma conexión SQLite
/// in-memory (mismo patrón de medición que <c>docs/benchmark-multitenancy.md</c>, F1-11), el camino
/// genérico (<see cref="IReadRepository{TEntity,TId}.CountAsync"/> + <c>ListAsync(spec, selector)</c>
/// sumado en memoria) contra el hot path especializado (un único <c>SELECT COUNT/SUM</c>). Los números
/// de una corrida de referencia están documentados en <c>docs/guia-hot-paths.md</c>.
/// </summary>
public class HotPathBenchmarkTests : IDisposable
{
    private const int RowCount = 5_000;
    private const int Threshold = 2_500;
    private const int Iterations = 50;

    private readonly ITestOutputHelper _output;
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _context;

    public HotPathBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TestDbContext(options);
        _context.Database.EnsureCreated();

        var random = new Random(Seed: 42);
        var entities = Enumerable.Range(0, RowCount)
            .Select(i => new TestEntity(Guid.NewGuid(), $"Entity-{i}", random.Next(0, 5_000)))
            .ToList();
        _context.TestEntities.AddRange(entities);
        _context.SaveChanges();
    }

    [Fact]
    public async Task GenericPathAndHotPath_ProduceTheSameResult()
    {
        var repository = new ReadOnlyRepositoryBase<TestEntity, Guid>(_context);
        var executor = new HotPathQueryExecutor(_context);
        var spec = new ByAmountAboveSpecification(Threshold);

        var genericTotal = await repository.CountAsync(spec);
        var genericSum = (await repository.ListAsync(spec, e => e.Amount)).Sum();

        var hotPathResult = await executor.SingleOrDefaultAsync(new ResumenAmountHotPathQuery(Threshold));

        hotPathResult.Should().NotBeNull();
        hotPathResult!.Total.Should().Be(genericTotal);
        hotPathResult.SumaAmount.Should().Be(genericSum);
    }

    [Fact]
    public async Task Benchmark_GenericSpecificationPath_Vs_HotPath()
    {
        var repository = new ReadOnlyRepositoryBase<TestEntity, Guid>(_context);
        var executor = new HotPathQueryExecutor(_context);
        var spec = new ByAmountAboveSpecification(Threshold);
        var hotPathQuery = new ResumenAmountHotPathQuery(Threshold);

        // Warm-up: evita que la primera compilación de query LINQ/plan SQLite distorsione la medición.
        _ = await repository.CountAsync(spec);
        _ = (await repository.ListAsync(spec, e => e.Amount)).Sum();
        _ = await executor.SingleOrDefaultAsync(hotPathQuery);

        var genericStopwatch = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            var total = await repository.CountAsync(spec);
            var amounts = await repository.ListAsync(spec, e => e.Amount);
            _ = amounts.Sum();
            _ = total;
        }

        genericStopwatch.Stop();

        var hotPathStopwatch = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            _ = await executor.SingleOrDefaultAsync(hotPathQuery);
        }

        hotPathStopwatch.Stop();

        var genericAvgMs = genericStopwatch.Elapsed.TotalMilliseconds / Iterations;
        var hotPathAvgMs = hotPathStopwatch.Elapsed.TotalMilliseconds / Iterations;
        var speedup = genericAvgMs / hotPathAvgMs;

        _output.WriteLine(
            $"RowCount={RowCount}, Threshold={Threshold}, Iterations={Iterations} | " +
            $"Generico (CountAsync+ListAsync+Sum): {genericStopwatch.Elapsed.TotalMilliseconds:F2} ms total, " +
            $"{genericAvgMs:F4} ms/iteración | " +
            $"HotPath (SELECT COUNT/SUM): {hotPathStopwatch.Elapsed.TotalMilliseconds:F2} ms total, " +
            $"{hotPathAvgMs:F4} ms/iteración | Speedup: {speedup:F1}x");

        // No se fija un ratio exacto (el entorno de CI compartido introduce variabilidad de E/S) — el
        // invariante que sí es razonable exigir es que transferir 1 fila agregada en la base de datos
        // sea más rápido que transferir ~la mitad de 5.000 filas para sumarlas en memoria del lado de
        // la aplicación. Los números de una corrida de referencia (3 corridas, política de
        // docs/entorno-referencia.md) están documentados en docs/guia-hot-paths.md.
        hotPathAvgMs.Should().BeLessThan(genericAvgMs);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
