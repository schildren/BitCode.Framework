using BitCode.Framework.Shared.Testing;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "SqlServer integration tests";
}
