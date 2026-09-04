using Testcontainers.MsSql;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

public class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "SqlServer integration tests";
}
