using Confluent.Kafka;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Traduce <see cref="KafkaMessagingOptions"/> a los objetos de configuración concretos de
/// <c>Confluent.Kafka</c> (<see cref="ProducerConfig"/>/<see cref="ConsumerConfig"/>), en un único
/// lugar, para que el productor y el consumidor apliquen siempre la misma configuración de
/// transporte/autenticación (evita que uno quede con SASL/SSL configurado y el otro no).
/// </summary>
/// <remarks>
/// F3-11: mapea las 4 combinaciones de <see cref="SecurityProtocol"/> que soporta
/// <c>Confluent.Kafka.ClientConfig</c> (Plaintext, SaslPlaintext, Ssl, SaslSsl) — el valor de
/// <see cref="KafkaMessagingOptions.SecurityProtocol"/> se traslada siempre tal cual a
/// <c>ClientConfig.SecurityProtocol</c>, y las credenciales/certificados solo se agregan si están
/// configurados. No valida consistencia acá (eso es responsabilidad de
/// <see cref="KafkaMessagingOptionsValidator"/>, invocado antes en <c>AddSharedMessagingKafka</c>) para
/// mantener esta clase como un mapeo puro sin lógica de negocio.
/// </remarks>
public static class KafkaClientConfigFactory
{
    public static ProducerConfig BuildProducerConfig(KafkaMessagingOptions options)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
        };

        ApplySecurity(config, options);

        return config;
    }

    public static ConsumerConfig BuildConsumerConfig(KafkaMessagingOptions options, string? consumerGroupIdOverride = null)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
            GroupId = consumerGroupIdOverride ?? options.ConsumerGroupId
                ?? throw new InvalidOperationException(
                    $"No se especificó {nameof(KafkaMessagingOptions.ConsumerGroupId)} en la configuración " +
                    $"({KafkaMessagingOptions.SectionName}) ni un group id explícito para este consumidor."),
            // F3-04 (Inbox Consumer) es quien decide cuándo confirmar un mensaje procesado; hasta que
            // exista ese relay, KafkaEventConsumer<TEvent> confirma manualmente offset por offset
            // después de que el handler de negocio termina con éxito (ver KafkaEventConsumer<TEvent>).
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };

        ApplySecurity(config, options);

        return config;
    }

    private static void ApplySecurity(ClientConfig config, KafkaMessagingOptions options)
    {
        config.SecurityProtocol = options.SecurityProtocol;

        if (options.SaslMechanism is not null)
        {
            config.SaslMechanism = options.SaslMechanism;
            config.SaslUsername = options.SaslUsername;
            config.SaslPassword = options.SaslPassword;
        }

        if (!string.IsNullOrWhiteSpace(options.SslCaLocation))
        {
            config.SslCaLocation = options.SslCaLocation;
        }

        if (!string.IsNullOrWhiteSpace(options.SslCertificateLocation))
        {
            config.SslCertificateLocation = options.SslCertificateLocation;
        }

        if (!string.IsNullOrWhiteSpace(options.SslKeyLocation))
        {
            config.SslKeyLocation = options.SslKeyLocation;
            config.SslKeyPassword = options.SslKeyPassword;
        }
    }
}
