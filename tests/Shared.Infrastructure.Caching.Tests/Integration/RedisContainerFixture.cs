using Testcontainers.Redis;

namespace BitCode.Framework.Shared.Infrastructure.Caching.Tests.Integration;

public class RedisContainerFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder().Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public class RedisCollection : ICollectionFixture<RedisContainerFixture>
{
    public const string Name = "Redis caching integration tests";
}
