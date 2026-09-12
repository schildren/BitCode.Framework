using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Sharding;

/// <summary>
/// F1-14 (estrategia T3, prototipo de operación): pruebas del mecanismo de aplicar una migración a
/// N bases de datos dedicadas, una por tenant, sin conectar infraestructura real (SQLite en memoria
/// simula cada base dedicada).
/// </summary>
public class DedicatedTenantDatabaseMigratorTests
{
    private sealed class FakeDedicatedTenantDbContext(DbContextOptions<FakeDedicatedTenantDbContext> options)
        : DbContext(options)
    {
        public DbSet<FakeDedicatedTenantEntity> Entities => Set<FakeDedicatedTenantEntity>();
    }

    private sealed class FakeDedicatedTenantEntity
    {
        public Guid Id { get; set; }
    }

    /// <summary>
    /// Cada tenant T3 tiene, por definición, un shard exclusivo (cardinalidad 1): a diferencia del
    /// prototipo de T2 (<c>TenantShardMapResolver</c>), donde varios tenants pueden compartir el
    /// mismo <see cref="ShardId"/>, este resolver de prueba deriva el <see cref="ShardId"/>
    /// directamente del <c>tenantId</c> (determinístico, sin tabla de mapeo) para simular T3.
    /// </summary>
    private sealed class OneShardPerTenantResolver : IShardResolver
    {
        public Task<ShardId> ResolveShardAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(new ShardId(tenantId.ToString()));
    }

    private sealed class InMemorySqliteConnectionStringProvider(IReadOnlyDictionary<ShardId, string> connectionStrings)
        : IShardConnectionStringProvider
    {
        public Task<string> GetConnectionStringAsync(ShardId shardId, CancellationToken cancellationToken) =>
            Task.FromResult(connectionStrings[shardId]);
    }

    private static Task MigrateWithEnsureCreatedAsync(string connectionString, CancellationToken cancellationToken)
    {
        // Prototipo: el repositorio no tiene todavía migraciones de EF Core compiladas para un
        // DbContext de prueba, así que EnsureCreatedAsync hace las veces de "aplicar el esquema
        // vigente" para demostrar el mecanismo de iteración + aplicación por conexión. Un proyecto
        // consumidor real reemplaza este delegado por uno que llame
        // `new MiAppDbContext(...).Database.MigrateAsync(cancellationToken)`.
        var options = new DbContextOptionsBuilder<FakeDedicatedTenantDbContext>()
            .UseSqlite(connectionString)
            .Options;
        using var context = new FakeDedicatedTenantDbContext(options);
        return context.Database.EnsureCreatedAsync(cancellationToken);
    }

    [Fact]
    public async Task MigrateAllAsync_NoDedicatedTenants_ReturnsEmptySuccessfulReport()
    {
        var catalog = new InMemoryDedicatedTenantDatabaseCatalog();
        var migrator = new DedicatedTenantDatabaseMigrator(
            catalog,
            new OneShardPerTenantResolver(),
            new InMemorySqliteConnectionStringProvider(new Dictionary<ShardId, string>()));

        var report = await migrator.MigrateAllAsync(MigrateWithEnsureCreatedAsync, CancellationToken.None);

        report.Results.Should().BeEmpty();
        report.AllSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAllAsync_ThreeDedicatedTenants_MigratesEachOneAgainstItsOwnDatabase()
    {
        var tenantIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var catalog = new InMemoryDedicatedTenantDatabaseCatalog();
        var connectionStrings = new Dictionary<ShardId, string>();
        foreach (var tenantId in tenantIds)
        {
            catalog.Register(tenantId);
            connectionStrings[new ShardId(tenantId.ToString())] =
                $"Data Source=file:{tenantId:N}?mode=memory&cache=shared";
        }

        var resolver = new OneShardPerTenantResolver();
        var provider = new InMemorySqliteConnectionStringProvider(connectionStrings);
        var migrator = new DedicatedTenantDatabaseMigrator(catalog, resolver, provider);

        // Cada base "en memoria compartida" de SQLite necesita al menos una conexión abierta durante
        // toda la prueba para no perder el esquema apenas se cierre la última conexión.
        var keepAliveConnections = connectionStrings.Values
            .Select(connectionString =>
            {
                var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
                connection.Open();
                return connection;
            })
            .ToList();
        try
        {
            var report = await migrator.MigrateAllAsync(MigrateWithEnsureCreatedAsync, CancellationToken.None);

            report.AllSucceeded.Should().BeTrue();
            report.Results.Should().HaveCount(3);
            report.Results.Select(result => result.TenantId).Should().BeEquivalentTo(tenantIds);

            foreach (var connectionString in connectionStrings.Values)
            {
                var options = new DbContextOptionsBuilder<FakeDedicatedTenantDbContext>()
                    .UseSqlite(connectionString)
                    .Options;
                await using var context = new FakeDedicatedTenantDbContext(options);
                // Si EnsureCreatedAsync no aplicó el esquema en esta base dedicada, consultar la
                // tabla lanza (SqliteException: no such table). Que la consulta tenga éxito (aunque
                // devuelva cero filas) es la prueba de que la migración se aplicó a esta base
                // concreta, no a otra.
                var rowCount = await context.Entities.CountAsync();
                rowCount.Should().Be(0);
            }
        }
        finally
        {
            foreach (var connection in keepAliveConnections)
            {
                connection.Dispose();
            }
        }
    }

    [Fact]
    public async Task MigrateAllAsync_OneTenantFailsToMigrate_DoesNotStopTheRemainingTenants()
    {
        var succeedingTenantId = Guid.NewGuid();
        var failingTenantId = Guid.NewGuid();
        var catalog = new InMemoryDedicatedTenantDatabaseCatalog();
        catalog.Register(succeedingTenantId);
        catalog.Register(failingTenantId);

        var succeedingShard = new ShardId(succeedingTenantId.ToString());
        var failingShard = new ShardId(failingTenantId.ToString());
        var connectionStrings = new Dictionary<ShardId, string>
        {
            [succeedingShard] = $"Data Source=file:{succeedingTenantId:N}?mode=memory&cache=shared",
            // Cadena de conexión inválida a propósito: simula un tenant cuya base dedicada no está
            // disponible (por ejemplo, servidor caído o credenciales rotadas) sin necesitar
            // infraestructura real.
            [failingShard] = "Data Source=/ruta/que/no/existe/no-tal-carpeta/tenant.db;Mode=ReadOnly",
        };

        var resolver = new OneShardPerTenantResolver();
        var provider = new InMemorySqliteConnectionStringProvider(connectionStrings);
        var migrator = new DedicatedTenantDatabaseMigrator(catalog, resolver, provider);

        using var keepAlive = new Microsoft.Data.Sqlite.SqliteConnection(connectionStrings[succeedingShard]);
        keepAlive.Open();

        var report = await migrator.MigrateAllAsync(MigrateWithEnsureCreatedAsync, CancellationToken.None);

        report.AllSucceeded.Should().BeFalse();
        report.Failures.Should().ContainSingle(result => result.TenantId == failingTenantId);
        report.Results.Should().Contain(result => result.TenantId == succeedingTenantId && result.Succeeded);
    }
}
