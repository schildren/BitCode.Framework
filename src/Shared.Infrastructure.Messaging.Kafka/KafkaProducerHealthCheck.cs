using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Health check de READINESS (F9-05) para el <see cref="IProducer{TKey,TValue}"/> singleton que
/// registra <see cref="KafkaServiceCollectionExtensions.AddSharedMessagingKafka"/> -- mismo espíritu que
/// los health checks de <c>*DbContextHealthCheck</c> (F1-25) de cada módulo: se registra siempre con el
/// tag "ready", nunca en <c>/health/live</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IProducer{TKey,TValue}"/> no expone <c>GetMetadata</c> directamente -- ese método vive en
/// <see cref="IAdminClient"/>. Este check crea un <see cref="DependentAdminClientBuilder"/> a partir del
/// mismo <see cref="Handle"/> (<see cref="IProducer{TKey,TValue}.Handle"/>) que ya usa el productor
/// singleton registrado por <see cref="KafkaServiceCollectionExtensions.AddSharedMessagingKafka"/> --
/// NO abre una conexión ni un cliente nuevo al broker, reutiliza el canal ya establecido.
/// </para>
/// <para>
/// Deliberadamente superficial: pide al broker los metadatos de TODO el clúster sin producir ningún
/// mensaje ni tocar un tópico concreto. Esto confirma que el proceso puede alcanzar y autenticarse
/// contra el clúster Kafka configurado (<see cref="KafkaMessagingOptions.BootstrapServers"/>), pero NO
/// confirma que un tópico específico exista, que el broker tenga particiones suficientes, ni que la
/// publicación real a través del Outbox (<c>OutboxBatchProcessor</c>) vaya a funcionar bajo carga -- ver
/// la limitación documentada en <c>docs/guia-workflow.md</c>, sección "Host independiente (F9-05)".
/// </para>
/// </remarks>
internal sealed class KafkaProducerHealthCheck(IProducer<string, byte[]> producer) : IHealthCheck
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(5);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var adminClient = new DependentAdminClientBuilder(producer.Handle).Build();
            var metadata = adminClient.GetMetadata(MetadataTimeout);

            return Task.FromResult(metadata.Brokers.Count > 0
                ? HealthCheckResult.Healthy($"Kafka disponible ({metadata.Brokers.Count} broker(s)).")
                : HealthCheckResult.Unhealthy("Kafka no reportó ningún broker en los metadatos del clúster."));
        }
        catch (Exception ex) when (ex is KafkaException or OperationCanceledException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Kafka no responde.", ex));
        }
    }
}
