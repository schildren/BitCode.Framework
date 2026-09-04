using BitCode.Framework.Shared.Testing;

namespace BitCode.Framework.Shared.Infrastructure.Caching.Tests.Integration;

[CollectionDefinition(Name)]
public class RedisCollection : ICollectionFixture<RedisContainerFixture>
{
    public const string Name = "Redis caching integration tests";
}
