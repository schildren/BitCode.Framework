using BitCode.Framework.Shared.Testing;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F3-04 (Inbox Consumer): <see cref="InboxConsumerIntegrationTests"/> necesita SQL Server real (donde
/// vive <c>InboxMessage</c>, F1-24) Y un broker Kafka real (F3-02) en la misma clase de test — mismo
/// patrón de composición de fixtures que <see cref="OutboxPublisherCollection"/> (F3-03).
/// </summary>
[CollectionDefinition(Name)]
public class InboxConsumerCollection : ICollectionFixture<SqlServerContainerFixture>, ICollectionFixture<KafkaContainerFixture>
{
    public const string Name = "InboxConsumer integration tests";
}
