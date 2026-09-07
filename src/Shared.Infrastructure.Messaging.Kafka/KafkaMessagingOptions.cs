using Confluent.Kafka;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Configuración tipada del adapter Kafka (F3-02), inyectable vía <c>IConfiguration</c> ("Messaging:Kafka"),
/// siguiendo el mismo patrón de opciones que <c>CachingOptions</c> (Shared.Infrastructure.Caching).
/// </summary>
/// <remarks>
/// Autenticación/transporte: por defecto <see cref="SecurityProtocol.Plaintext"/> (sin cifrado ni
/// autenticación), el único modo usado en local/CI (fixture de Testcontainers, sin SASL/SSL) — sigue
/// siendo así después de F3-11: no hay imagen de Testcontainers con SASL/ACL habilitado en este
/// repositorio, así que ningún test de integración ejercita todavía SASL/SSL contra un broker real.
/// F3-11 ("Seguridad — TLS, ACL, identidad y mínimo privilegio") completa el mapeo de estas propiedades
/// hacia <c>Confluent.Kafka.ClientConfig</c> (<see cref="KafkaClientConfigFactory"/>) y agrega
/// <see cref="KafkaMessagingOptionsValidator"/> para fallar rápido ante una combinación inconsistente
/// (p. ej. <see cref="SaslMechanism"/> configurado con <see cref="SecurityProtocol.Plaintext"/>); la
/// verificación de que un cliente con credenciales/ACL incorrectos es efectivamente rechazado por el
/// broker (criterio de aceptación "Acceso cruzado denegado") queda documentada como parte del runbook
/// operativo (<c>docs/politica-seguridad-kafka.md</c>), no de un test automatizado — ver ese documento
/// para el detalle de qué se verificó con broker real y qué no.
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

    /// <summary>
    /// Protocolo de seguridad de transporte. Por defecto <see cref="SecurityProtocol.Plaintext"/>
    /// (local/CI). En productivo debe ser <see cref="SecurityProtocol.SaslSsl"/> (recomendado,
    /// ver <c>docs/politica-seguridad-kafka.md</c>); las combinaciones se validan en el arranque
    /// (<see cref="KafkaMessagingOptionsValidator"/>).
    /// </summary>
    public SecurityProtocol SecurityProtocol { get; set; } = SecurityProtocol.Plaintext;

    /// <summary>Mecanismo SASL, si <see cref="SecurityProtocol"/> lo requiere (SaslPlaintext/SaslSsl). Null = sin SASL.</summary>
    public SaslMechanism? SaslMechanism { get; set; }

    /// <summary>
    /// Usuario SASL (identidad del cliente frente al broker, base de las ACL por tópico). Nunca debe
    /// provenir de un valor hardcodeado en el repositorio: el host que compone <see cref="KafkaMessagingOptions"/>
    /// es responsable de resolverlo desde un secreto externo (p. ej. <c>ISecretProvider</c> de
    /// <c>Shared.Infrastructure.Security</c>, F2-12) antes de asignarlo acá — este proyecto
    /// (<c>Shared.Infrastructure.Messaging.Kafka</c>) no depende de <c>Shared.Infrastructure.Security</c>
    /// (mismo criterio de bajo acoplamiento que F3-10 con Observability), ver <c>docs/politica-seguridad-kafka.md</c>.
    /// </summary>
    public string? SaslUsername { get; set; }

    /// <summary>Password/token SASL. Mismo origen y misma prohibición de hardcodeo que <see cref="SaslUsername"/>.</summary>
    public string? SaslPassword { get; set; }

    /// <summary>Ruta al certificado CA para verificar el broker cuando el protocolo usa SSL (Ssl/SaslSsl).</summary>
    public string? SslCaLocation { get; set; }

    /// <summary>
    /// Ruta al certificado de cliente (PEM/PKCS12) para autenticación mutua TLS (mTLS), cuando el
    /// broker mapea el "SSL principal" del certificado a un principal de ACL en vez de (o además de)
    /// SASL. Opcional — la mayoría de los despliegues productivos de este framework usan SASL_SSL con
    /// SCRAM en vez de mTLS (ver <c>docs/politica-seguridad-kafka.md</c>).
    /// </summary>
    public string? SslCertificateLocation { get; set; }

    /// <summary>Ruta a la clave privada del certificado de cliente, pareja de <see cref="SslCertificateLocation"/>.</summary>
    public string? SslKeyLocation { get; set; }

    /// <summary>Password de la clave privada de <see cref="SslKeyLocation"/>, si está cifrada. Mismo origen que <see cref="SaslPassword"/>.</summary>
    public string? SslKeyPassword { get; set; }
}
