using BitCode.Framework.Shared.Testing;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests.Integration;

[CollectionDefinition(Name)]
public class KafkaCollection : ICollectionFixture<KafkaContainerFixture>
{
    public const string Name = "Kafka messaging integration tests";
}
