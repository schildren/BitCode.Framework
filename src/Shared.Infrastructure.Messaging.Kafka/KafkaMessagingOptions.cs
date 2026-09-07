using Confluent.Kafka;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Configuración tipada del adapter Kafka (F3-02), inyectable vía <c>IConfiguration</c> ("Messaging:Kafka"),
/// siguiendo el mismo patrón de opciones que <c>CachingOptions</c> (Shared.Infrastructure.Caching).
/// </summary>
/// <remarks>
/// Autenticación/transporte: por defecto <see cref="SecurityProtocol.Plaintext"/> (sin cifrado ni
/// autenticación), el único modo usado en local/CI (fixture de Testcontainers, sin SASL/SSL). Las
/// propiedades <see cref="SecurityProtocol"/>, <see cref="SaslMechanism"/>, <see cref="SaslUsername"/>,
/// <see cref="SaslPassword"/> y <see cref="SslCaLocation"/> dejan preparado el punto de extensión que
/// <c>Confluent.Kafka.ClientConfig</c> ya soporta (SASL_SSL/SSL con CA propia) para cuando F3-11
/// ("Seguridad — TLS, ACL, identidad y mínimo privilegio") defina la configuración productiva real;
/// F3-02 no la implementa ni la valida contra un broker con TLS/SASL habilitado.
/// </remarks>
public sealed class KafkaMessagingOptions
{
    public const string SectionName = "Messaging:Kafka";

    /// <summary>Lista de brokers "host:puerto" separados por coma (<c>ClientConfig.BootstrapServers</c>).</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Identificador de cliente reportado al broker (<c>ClientConfig.ClientId</c>); opcional.</summary>
    public string? ClientId { get; set; }

    /// <summary>Group id por defecto para consumidores que no indiquen uno explícito.</summary>
    public string? ConsumerGroupId { get; set; }

    /// <summary>Protocolo de seguridad de transporte. Por defecto <see cref="SecurityProtocol.Plaintext"/> (local/CI); TLS/SASL productivo es F3-11.</summary>
    public SecurityProtocol SecurityProtocol { get; set; } = SecurityProtocol.Plaintext;

    /// <summary>Mecanismo SASL, si <see cref="SecurityProtocol"/> lo requiere (SaslPlaintext/SaslSsl). Null = sin SASL.</summary>
    public SaslMechanism? SaslMechanism { get; set; }

    /// <summary>Usuario SASL. Nunca debe provenir de un valor hardcodeado en el repositorio (secreto externo, ver docs/convenciones.md).</summary>
    public string? SaslUsername { get; set; }

    /// <summary>Password SASL. Nunca debe provenir de un valor hardcodeado en el repositorio (secreto externo, ver docs/convenciones.md).</summary>
    public string? SaslPassword { get; set; }

    /// <summary>Ruta al certificado CA para verificar el broker cuando el protocolo usa SSL (Ssl/SaslSsl).</summary>
    public string? SslCaLocation { get; set; }
}
