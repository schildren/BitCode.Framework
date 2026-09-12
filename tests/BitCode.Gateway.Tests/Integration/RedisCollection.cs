using BitCode.Framework.Shared.Testing;

namespace BitCode.Gateway.Tests.Integration;

[CollectionDefinition(Name)]
public class RedisCollection : ICollectionFixture<RedisContainerFixture>
{
    public const string Name = "Gateway distributed rate limiting integration tests";
}
