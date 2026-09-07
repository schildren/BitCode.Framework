using Confluent.Kafka;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Valida la coherencia de la configuración de seguridad de transporte de <see cref="KafkaMessagingOptions"/>
/// (F3-11). Se invoca desde <c>AddSharedMessagingKafka</c>, en el momento de registrar los servicios
/// (arranque), nunca en tiempo de request — mismo criterio de
/// "fail-fast en el arranque" que <c>PrivilegedOperationsOptionsValidator</c>
/// (<c>Shared.Infrastructure.Security</c>): una configuración de seguridad inconsistente para Kafka
/// (por ejemplo, un mecanismo SASL configurado con <see cref="SecurityProtocol.Plaintext"/>, que
/// <c>Confluent.Kafka</c> simplemente ignora en silencio) es un error de configuración de despliegue,
/// no un caso de negocio a manejar con <c>Result.Failure</c>: debe frenar el arranque del proceso con
/// un mensaje claro en vez de dejar un cliente Kafka corriendo sin la autenticación que el operador
/// creía haber configurado.
/// </summary>
public static class KafkaMessagingOptionsValidator
{
    /// <summary>
    /// Valida <paramref name="options"/>. Lanza <see cref="InvalidOperationException"/> con un mensaje
    /// que identifica exactamente qué combinación es inconsistente si la validación falla.
    /// </summary>
    public static void Validate(KafkaMessagingOptions options)
    {
        var usesSasl = options.SecurityProtocol is SecurityProtocol.SaslPlaintext or SecurityProtocol.SaslSsl;
        var hasSaslMechanism = options.SaslMechanism is not null;
        var hasSaslUsername = !string.IsNullOrWhiteSpace(options.SaslUsername);
        var hasSaslPassword = !string.IsNullOrWhiteSpace(options.SaslPassword);

        if (hasSaslMechanism && !usesSasl)
        {
            throw new InvalidOperationException(
                $"{KafkaMessagingOptions.SectionName}: {nameof(KafkaMessagingOptions.SaslMechanism)} " +
                $"está configurado ({options.SaslMechanism}) pero {nameof(KafkaMessagingOptions.SecurityProtocol)} " +
                $"es {options.SecurityProtocol}, que Confluent.Kafka no usa para autenticar SASL — " +
                $"el cliente se conectaría sin autenticación real, ignorando en silencio las credenciales " +
                $"configuradas. Usá {nameof(SecurityProtocol.SaslPlaintext)} o {nameof(SecurityProtocol.SaslSsl)}.");
        }

        if (usesSasl && !hasSaslMechanism)
        {
            throw new InvalidOperationException(
                $"{KafkaMessagingOptions.SectionName}: {nameof(KafkaMessagingOptions.SecurityProtocol)} es " +
                $"{options.SecurityProtocol}, que requiere {nameof(KafkaMessagingOptions.SaslMechanism)} " +
                "configurado (por ejemplo ScramSha256/ScramSha512/Plain) y no lo está.");
        }

        if (usesSasl && (!hasSaslUsername || !hasSaslPassword))
        {
            throw new InvalidOperationException(
                $"{KafkaMessagingOptions.SectionName}: {nameof(KafkaMessagingOptions.SecurityProtocol)} es " +
                $"{options.SecurityProtocol} con {nameof(KafkaMessagingOptions.SaslMechanism)}={options.SaslMechanism}, " +
                $"pero {nameof(KafkaMessagingOptions.SaslUsername)}/{nameof(KafkaMessagingOptions.SaslPassword)} " +
                "no están configurados (o vienen vacíos) — resolvélos desde un secreto externo antes de " +
                "registrar la configuración (ver docs/politica-seguridad-kafka.md).");
        }

        if (!usesSasl && (hasSaslUsername || hasSaslPassword))
        {
            throw new InvalidOperationException(
                $"{KafkaMessagingOptions.SectionName}: {nameof(KafkaMessagingOptions.SaslUsername)} o " +
                $"{nameof(KafkaMessagingOptions.SaslPassword)} están configurados pero " +
                $"{nameof(KafkaMessagingOptions.SecurityProtocol)} es {options.SecurityProtocol} (sin SASL) — " +
                "esas credenciales nunca se usarían, probable error de configuración (protocolo incorrecto " +
                "o credenciales sobrantes de una configuración anterior).");
        }

        var hasSslCertificate = !string.IsNullOrWhiteSpace(options.SslCertificateLocation);
        var hasSslKey = !string.IsNullOrWhiteSpace(options.SslKeyLocation);

        if (hasSslCertificate != hasSslKey)
        {
            throw new InvalidOperationException(
                $"{KafkaMessagingOptions.SectionName}: para autenticación mutua TLS (mTLS) hacen falta " +
                $"{nameof(KafkaMessagingOptions.SslCertificateLocation)} y {nameof(KafkaMessagingOptions.SslKeyLocation)} " +
                "juntos — solo uno de los dos está configurado.");
        }
    }
}
