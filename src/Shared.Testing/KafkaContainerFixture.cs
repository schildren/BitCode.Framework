using Testcontainers.Kafka;
using Xunit;

namespace BitCode.Framework.Shared.Testing;

/// <summary>Fixture de xUnit reutilizable para tests de integración que necesitan un broker Kafka real (F3-02).</summary>
public class KafkaContainerFixture : IAsyncLifetime
{
    private readonly KafkaContainer _container = new KafkaBuilder().Build();

    /// <summary>Lista de brokers "host:puerto" alcanzable desde el proceso de test (fuera del contenedor).</summary>
    public string BootstrapServers => _container.GetBootstrapAddress();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
