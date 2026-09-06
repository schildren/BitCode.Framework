using BitCode.Framework.Shared.Testing;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Integration;

[CollectionDefinition(Name)]
public class RedisCollection : ICollectionFixture<RedisContainerFixture>
{
    public const string Name = "Redis permission cache integration tests";
}
