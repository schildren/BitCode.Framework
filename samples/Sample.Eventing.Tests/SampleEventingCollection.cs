using BitCode.Framework.Shared.Testing;

namespace Sample.Eventing.Tests;

/// <summary>
/// F3-13 (prueba de referencia, cierre de la Fase 3): la prueba de referencia necesita SQL Server real
/// (donde viven <c>OutboxMessage</c>/<c>InboxMessage</c>, F1-23/F1-24, y las tablas de negocio de ambos
/// módulos de ejemplo) Y un broker Kafka real (F3-02) en la misma clase de test — mismo patrón de
/// composición de fixtures que <c>OutboxPublisherCollection</c>/<c>InboxConsumerCollection</c>
/// (Shared.Infrastructure.Persistence.Tests).
/// </summary>
[CollectionDefinition(Name)]
public class SampleEventingCollection : ICollectionFixture<SqlServerContainerFixture>, ICollectionFixture<KafkaContainerFixture>
{
    public const string Name = "Sample.Eventing reference integration tests";
}
