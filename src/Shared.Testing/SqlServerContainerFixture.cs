using Testcontainers.MsSql;
using Xunit;

namespace BitCode.Framework.Shared.Testing;

/// <summary>
/// Fixture de xUnit reutilizable para tests de integración que necesitan SQL Server real. Un
/// contenedor por clase de test (ver ICollectionFixture) — usar BuildIsolatedConnectionString para
/// que cada test opere sobre su propia base de datos dentro del mismo contenedor.
/// </summary>
public class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
