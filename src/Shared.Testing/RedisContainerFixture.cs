using Testcontainers.Redis;
using Xunit;

namespace BitCode.Framework.Shared.Testing;

/// <summary>Fixture de xUnit reutilizable para tests de integración que necesitan Redis real.</summary>
public class RedisContainerFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder().Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
