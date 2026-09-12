using BitCode.Framework.Shared.Application.Eventing;
using Confluent.Kafka;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests;

/// <summary>
/// F3-07 (Retries): clasificación transitorio/permanente de <see cref="ProduceException{TKey,TValue}"/>
/// — sin broker real, construye la excepción directamente con distintos <see cref="Error"/>.
/// </summary>
public class KafkaEventPublishFailureClassifierTests
{
    private readonly KafkaEventPublishFailureClassifier _classifier = new();

    [Theory]
    [InlineData(ErrorCode.BrokerNotAvailable)]
    [InlineData(ErrorCode.RequestTimedOut)]
    [InlineData(ErrorCode.NetworkException)]
    [InlineData(ErrorCode.NotEnoughReplicas)]
    [InlineData(ErrorCode.Local_Transport)]
    [InlineData(ErrorCode.Local_TimedOut)]
    [InlineData(ErrorCode.Local_AllBrokersDown)]
    public void Classify_TransientBrokerErrors_ReturnsTransient(ErrorCode errorCode)
    {
        var exception = BuildProduceException(errorCode);

        _classifier.Classify(exception).Should().Be(EventPublishFailureKind.Transient);
    }

    [Theory]
    [InlineData(ErrorCode.MsgSizeTooLarge)]
    [InlineData(ErrorCode.InvalidMsg)]
    [InlineData(ErrorCode.InvalidMsgSize)]
    [InlineData(ErrorCode.RecordListTooLarge)]
    [InlineData(ErrorCode.TopicAuthorizationFailed)]
    [InlineData(ErrorCode.ClusterAuthorizationFailed)]
    public void Classify_PermanentMessageOrAuthorizationErrors_ReturnsPermanent(ErrorCode errorCode)
    {
        var exception = BuildProduceException(errorCode);

        _classifier.Classify(exception).Should().Be(EventPublishFailureKind.Permanent);
    }

    [Fact]
    public void Classify_FatalError_ReturnsPermanentRegardlessOfCode()
    {
        // Local_TimedOut normalmente clasifica Transient; marcado como fatal (IsFatal) debe ganar
        // igual, sin importar el código específico.
        var exception = BuildProduceException(ErrorCode.Local_TimedOut, isFatal: true);

        _classifier.Classify(exception).Should().Be(EventPublishFailureKind.Permanent);
    }

    [Fact]
    public void Classify_NonKafkaException_ReturnsTransientBySafeDefault()
    {
        var exception = new InvalidOperationException("Fallo antes de llegar al broker (por ejemplo, serialización).");

        _classifier.Classify(exception).Should().Be(EventPublishFailureKind.Transient);
    }

    [Fact]
    public void Classify_NullException_Throws()
    {
        var act = () => _classifier.Classify(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static ProduceException<string, byte[]> BuildProduceException(ErrorCode errorCode, bool isFatal = false)
    {
        var error = new Error(errorCode, reason: errorCode.ToString(), isFatal: isFatal);
        var deliveryResult = new DeliveryResult<string, byte[]>
        {
            Status = PersistenceStatus.NotPersisted,
        };

        return new ProduceException<string, byte[]>(error, deliveryResult);
    }
}
