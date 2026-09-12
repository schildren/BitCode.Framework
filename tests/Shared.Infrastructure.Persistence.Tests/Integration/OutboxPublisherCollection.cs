using BitCode.Framework.Shared.Testing;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F3-03: OutboxPublisherIntegrationTests necesita SQL Server real (donde vive OutboxMessage) Y un
/// broker Kafka real (donde debe aparecer el evento publicado) en la misma clase de test — una
/// colección de xUnit puede componer más de un <see cref="ICollectionFixture{TFixture}"/>.
/// </summary>
[CollectionDefinition(Name)]
public class OutboxPublisherCollection : ICollectionFixture<SqlServerContainerFixture>, ICollectionFixture<KafkaContainerFixture>
{
    public const string Name = "OutboxPublisher integration tests";
}
