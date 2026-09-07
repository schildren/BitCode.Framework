using Confluent.Kafka;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests;

/// <summary>
/// F3-11 (Seguridad — TLS, ACL, identidad y mínimo privilegio): sin broker real, verifica dos cosas
/// completamente testeables sin Testcontainers: (1) que <see cref="KafkaClientConfigFactory"/> mapea
/// correctamente cada combinación de <see cref="SecurityProtocol"/> soportada por
/// <c>Confluent.Kafka.ClientConfig</c>, y (2) que <see cref="KafkaMessagingOptionsValidator"/> rechaza
/// en el arranque las combinaciones de <see cref="KafkaMessagingOptions"/> inconsistentes que
/// <c>Confluent.Kafka</c> aceptaría en silencio sin autenticar realmente. La verificación de que un
/// broker real con SASL/ACL habilitado efectivamente rechaza credenciales/tópicos no autorizados queda
/// documentada como pendiente de un entorno con broker SASL real, fuera del alcance de
/// <c>Testcontainers.Kafka</c> para esta tarea (ver docs/politica-seguridad-kafka.md).
/// </summary>
public class KafkaClientConfigFactoryAndSecurityValidationTests
{
    [Fact]
    public void BuildProducerConfig_Plaintext_DoesNotSetSaslOrSslFields()
    {
        var options = new KafkaMessagingOptions { BootstrapServers = "broker:9092" };

        var config = KafkaClientConfigFactory.BuildProducerConfig(options);

        config.SecurityProtocol.Should().Be(SecurityProtocol.Plaintext);
        config.SaslMechanism.Should().BeNull();
        config.SaslUsername.Should().BeNull();
        config.SaslPassword.Should().BeNull();
        config.SslCaLocation.Should().BeNull();
    }

    [Fact]
    public void BuildProducerConfig_SaslPlaintext_MapsMechanismAndCredentials()
    {
        var options = new KafkaMessagingOptions
        {
            BootstrapServers = "broker:9092",
            SecurityProtocol = SecurityProtocol.SaslPlaintext,
            SaslMechanism = Confluent.Kafka.SaslMechanism.ScramSha512,
            SaslUsername = "svc-orders",
            SaslPassword = "s3cr3t",
        };

        var config = KafkaClientConfigFactory.BuildProducerConfig(options);

        config.SecurityProtocol.Should().Be(SecurityProtocol.SaslPlaintext);
        config.SaslMechanism.Should().Be(Confluent.Kafka.SaslMechanism.ScramSha512);
        config.SaslUsername.Should().Be("svc-orders");
        config.SaslPassword.Should().Be("s3cr3t");
    }

    [Fact]
    public void BuildConsumerConfig_SaslSsl_MapsMechanismCredentialsAndCa()
    {
        var options = new KafkaMessagingOptions
        {
            BootstrapServers = "broker:9093",
            ConsumerGroupId = "orders-consumer",
            SecurityProtocol = SecurityProtocol.SaslSsl,
            SaslMechanism = Confluent.Kafka.SaslMechanism.ScramSha256,
            SaslUsername = "svc-orders",
            SaslPassword = "s3cr3t",
            SslCaLocation = "/etc/kafka/ca.pem",
        };

        var config = KafkaClientConfigFactory.BuildConsumerConfig(options);

        config.SecurityProtocol.Should().Be(SecurityProtocol.SaslSsl);
        config.SaslMechanism.Should().Be(Confluent.Kafka.SaslMechanism.ScramSha256);
        config.SaslUsername.Should().Be("svc-orders");
        config.SaslPassword.Should().Be("s3cr3t");
        config.SslCaLocation.Should().Be("/etc/kafka/ca.pem");
    }

    [Fact]
    public void BuildProducerConfig_SslWithClientCertificate_MapsMutualTlsFields()
    {
        var options = new KafkaMessagingOptions
        {
            BootstrapServers = "broker:9093",
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = "/etc/kafka/ca.pem",
            SslCertificateLocation = "/etc/kafka/client.pem",
            SslKeyLocation = "/etc/kafka/client.key",
            SslKeyPassword = "keypass",
        };

        var config = KafkaClientConfigFactory.BuildProducerConfig(options);

        config.SecurityProtocol.Should().Be(SecurityProtocol.Ssl);
        config.SslCaLocation.Should().Be("/etc/kafka/ca.pem");
        config.SslCertificateLocation.Should().Be("/etc/kafka/client.pem");
        config.SslKeyLocation.Should().Be("/etc/kafka/client.key");
        config.SslKeyPassword.Should().Be("keypass");
        config.SaslMechanism.Should().BeNull();
    }

    [Fact]
    public void Validate_DefaultPlaintextOptions_DoesNotThrow()
    {
        var act = () => KafkaMessagingOptionsValidator.Validate(new KafkaMessagingOptions());

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_SaslMechanismWithPlaintextProtocol_Throws()
    {
        var options = new KafkaMessagingOptions
        {
            SecurityProtocol = SecurityProtocol.Plaintext,
            SaslMechanism = Confluent.Kafka.SaslMechanism.Plain,
            SaslUsername = "u",
            SaslPassword = "p",
        };

        var act = () => KafkaMessagingOptionsValidator.Validate(options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*SaslMechanism*");
    }

    [Fact]
    public void Validate_SaslProtocolWithoutMechanism_Throws()
    {
        var options = new KafkaMessagingOptions
        {
            SecurityProtocol = SecurityProtocol.SaslSsl,
        };

        var act = () => KafkaMessagingOptionsValidator.Validate(options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*SaslMechanism*");
    }

    [Theory]
    [InlineData(null, "p")]
    [InlineData("u", null)]
    [InlineData("", "")]
    public void Validate_SaslProtocolWithMissingCredentials_Throws(string? username, string? password)
    {
        var options = new KafkaMessagingOptions
        {
            SecurityProtocol = SecurityProtocol.SaslPlaintext,
            SaslMechanism = Confluent.Kafka.SaslMechanism.ScramSha256,
            SaslUsername = username,
            SaslPassword = password,
        };

        var act = () => KafkaMessagingOptionsValidator.Validate(options);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_CredentialsSetWithoutSaslProtocol_Throws()
    {
        var options = new KafkaMessagingOptions
        {
            SecurityProtocol = SecurityProtocol.Ssl,
            SaslUsername = "u",
            SaslPassword = "p",
        };

        var act = () => KafkaMessagingOptionsValidator.Validate(options);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_ValidSaslSslConfiguration_DoesNotThrow()
    {
        var options = new KafkaMessagingOptions
        {
            SecurityProtocol = SecurityProtocol.SaslSsl,
            SaslMechanism = Confluent.Kafka.SaslMechanism.ScramSha512,
            SaslUsername = "svc-orders",
            SaslPassword = "s3cr3t",
            SslCaLocation = "/etc/kafka/ca.pem",
        };

        var act = () => KafkaMessagingOptionsValidator.Validate(options);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_OnlyClientCertificateWithoutKey_Throws()
    {
        var options = new KafkaMessagingOptions
        {
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCertificateLocation = "/etc/kafka/client.pem",
        };

        var act = () => KafkaMessagingOptionsValidator.Validate(options);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_ClientCertificateAndKeyTogether_DoesNotThrow()
    {
        var options = new KafkaMessagingOptions
        {
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCertificateLocation = "/etc/kafka/client.pem",
            SslKeyLocation = "/etc/kafka/client.key",
        };

        var act = () => KafkaMessagingOptionsValidator.Validate(options);

        act.Should().NotThrow();
    }
}
