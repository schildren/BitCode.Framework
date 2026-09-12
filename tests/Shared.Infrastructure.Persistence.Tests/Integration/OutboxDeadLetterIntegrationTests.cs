using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Outbox;
using BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;
using BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.DeadLetter;
using BitCode.Framework.Shared.Kernel;
using BitCode.Framework.Shared.Testing;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F3-08 (DLQ): criterio de aceptación literal de la Fase 3 ("Reprocesamiento desde DLQ"), verificado
/// contra SQL Server real (donde vive <see cref="OutboxMessage"/>) y un broker Kafka real (tópico
/// dead-letter, <see cref="KafkaDeadLetterPublisher"/>) — un publisher forzado a fallar permanentemente
/// agota la fila, se verifica la copia en el tópico <c>.dlq</c>, se reprocesa con
/// <see cref="IDeadLetterReprocessor"/> (auditado, <see cref="IAuditWriter"/>) y se verifica que el
/// evento eventualmente se publica con éxito.
/// </summary>
[Collection(OutboxPublisherCollection.Name)]
public class OutboxDeadLetterIntegrationTests(SqlServerContainerFixture sqlFixture, KafkaContainerFixture kafkaFixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        sqlFixture.BuildIsolatedConnectionString("ObxDlq", testName);

    private KafkaMessagingOptions BuildKafkaOptions(string consumerGroupId) => new()
    {
        BootstrapServers = kafkaFixture.BootstrapServers,
        ClientId = "outbox-dlq-tests",
        ConsumerGroupId = consumerGroupId,
    };

    private async Task<(ServiceProvider Provider, TogglableEventPublisher Publisher, InMemoryAuditWriter AuditWriter)> BuildProviderAsync(
        string connectionString,
        KafkaMessagingOptions kafkaOptions)
    {
        var publisher = new TogglableEventPublisher();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        services.AddSingleton<IEventPublisher>(publisher);
        services.AddSingleton(new OutboxPublisherOptions { Retry = new EventRetryPolicyOptions { BaseDelay = TimeSpan.Zero } });
        // El clasificador de prueba (AlwaysPermanentClassifier, definido en OutboxPublisherRetryTests)
        // fuerza agotamiento en el PRIMER fallo, sin importar MaxAttempts -- exactamente lo que este
        // test necesita para llegar rápido al estado "agotado" que dispara la publicación a DLQ.
        services.AddSingleton<IEventPublishFailureClassifier, AlwaysPermanentClassifier>();
        services.AddScoped<OutboxBatchProcessor>();

        // Productor Kafka real (singleton, dispuesto junto con el ServiceProvider): usado por
        // KafkaDeadLetterPublisher para publicar la copia del mensaje agotado al tópico dead-letter.
        services.AddSingleton<IProducer<string, byte[]>>(
            new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(kafkaOptions)).Build());
        services.AddSingleton<IKafkaTopicNameResolver>(DefaultKafkaTopicNameResolver.Instance);
        services.AddSingleton<IDeadLetterPublisher, KafkaDeadLetterPublisher>();

        // F3-08 (Mitad 2): reprocesamiento auditado -- InMemoryAuditWriter (F2-15) alcanza para este
        // test de integración (verificar que la entrada de auditoría existe), no necesita un backend
        // persistente real.
        services.AddSharedAuditing();
        services.AddSharedDeadLetterReprocessing();

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        var auditWriter = provider.GetRequiredService<InMemoryAuditWriter>();

        return (provider, publisher, auditWriter);
    }

    private static async Task<OutboxMessage> SeedOutboxMessageAsync(IServiceProvider provider, object domainEvent)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Empty,
            EventType = domainEvent.GetType().AssemblyQualifiedName!,
            PayloadJson = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
            OccurredAtUtc = DateTime.UtcNow,
        };

        context.Set<OutboxMessage>().Add(message);
        await context.SaveChangesAsync();

        return message;
    }

    private static async Task<OutboxMessage?> FindOutboxMessageAsync(IServiceProvider provider, Guid id)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        return await context.Set<OutboxMessage>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(message => message.Id == id);
    }

    [Fact]
    public async Task ExhaustedMessage_DlqTopicAndAuditedReprocess_EventuallyPublishes()
    {
        var kafkaOptions = BuildKafkaOptions($"grupo-{Guid.NewGuid():N}");
        var (provider, publisher, auditWriter) = await BuildProviderAsync(BuildIsolatedConnectionString(), kafkaOptions);
        await using var _ = provider;

        var integrationEvent = new OutboxDeadLetterTestEvent(Guid.NewGuid(), "Evento que agota reintentos");
        var message = await SeedOutboxMessageAsync(provider, integrationEvent);

        // --- 1) Agotamiento: el clasificador Permanent fuerza ExhaustedAtUtc en el primer fallo. ---
        publisher.ShouldFail = true;
        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var result = await processor.ProcessBatchAsync();
            result.Claimed.Should().Be(1);
            result.Exhausted.Should().Be(1);
        }

        var afterExhaustion = await FindOutboxMessageAsync(provider, message.Id);
        afterExhaustion!.ExhaustedAtUtc.Should().NotBeNull("un error permanente agota la fila en el primer fallo (F3-07)");
        afterExhaustion.ProcessedAtUtc.Should().BeNull("una fila agotada nunca se marca como procesada -- nunca se pierde (F3-03/F3-07)");

        // --- 2) DLQ: la copia debe llegar al tópico dead-letter, con los headers bitcode-dlq-*. ---
        var dlqTopic = DefaultKafkaTopicNameResolver.Instance.ResolveTopicName(integrationEvent.EventType) + KafkaDeadLetterPublisher.DeadLetterTopicSuffix;
        var dlqMessage = await ConsumeSingleMessageAsync(kafkaOptions, dlqTopic, TimeSpan.FromSeconds(30));

        dlqMessage.Should().NotBeNull("OutboxBatchProcessor debe publicar una copia del mensaje agotado al tópico dead-letter");
        GetHeaderString(dlqMessage!.Message.Headers, KafkaDeadLetterPublisher.OriginalEventTypeHeader).Should().Be(integrationEvent.EventType);
        GetHeaderString(dlqMessage.Message.Headers, KafkaDeadLetterPublisher.SourceMessageIdHeader).Should().Be(message.Id.ToString());
        GetHeaderString(dlqMessage.Message.Headers, KafkaDeadLetterPublisher.AttemptsHeader).Should().Be("1");
        dlqMessage.Message.Value.Should().BeEquivalentTo(Encoding.UTF8.GetBytes(afterExhaustion.PayloadJson));

        // --- 3) Reprocesamiento auditado: IDeadLetterReprocessor reabre la fila y deja auditoría. ---
        await using (var scope = provider.CreateAsyncScope())
        {
            var reprocessor = scope.ServiceProvider.GetRequiredService<IDeadLetterReprocessor>();
            var reprocessResult = await reprocessor.ReprocessAsync(new DeadLetterReprocessRequest(
                message.Id,
                new AuditActor("operador-test", AuditActorType.User),
                "Causa raíz corregida (prueba de integración F3-08)."));

            reprocessResult.IsSuccess.Should().BeTrue();
            reprocessResult.Value.OutboxMessageId.Should().Be(message.Id);
            reprocessResult.Value.NewRetryCount.Should().Be(0);
        }

        var afterReprocess = await FindOutboxMessageAsync(provider, message.Id);
        afterReprocess!.ExhaustedAtUtc.Should().BeNull("el reprocesamiento reabre la fila para que vuelva a ser candidata de publicación");
        afterReprocess.RetryCount.Should().Be(0);

        auditWriter.Entries.Should().ContainSingle(entry =>
                entry.Action == "OutboxMessage.DeadLetterReprocess" &&
                entry.Resource.Type == "OutboxMessage" &&
                entry.Resource.Id == message.Id.ToString() &&
                entry.Outcome == AuditOutcome.Success)
            .Which.Actor.Id.Should().Be("operador-test");

        // --- 4) El evento eventualmente se publica con éxito tras el reprocesamiento. ---
        publisher.ShouldFail = false;
        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var result = await processor.ProcessBatchAsync();
            result.Claimed.Should().Be(1, "la fila reabierta vuelve a ser candidata en el próximo ciclo de sondeo");
            result.Published.Should().Be(1);
        }

        var afterRepublish = await FindOutboxMessageAsync(provider, message.Id);
        afterRepublish!.ProcessedAtUtc.Should().NotBeNull();
        publisher.PublishedEventIds.Should().ContainSingle().Which.Should().Be(integrationEvent.EventId);
    }

    [Fact]
    public async Task ReprocessAsync_MessageNotExhausted_FailsAndDoesNotMutate()
    {
        var kafkaOptions = BuildKafkaOptions($"grupo-{Guid.NewGuid():N}");
        var (provider, _, auditWriter) = await BuildProviderAsync(BuildIsolatedConnectionString(), kafkaOptions);
        await using var _ = provider;

        var integrationEvent = new OutboxDeadLetterTestEvent(Guid.NewGuid(), "Nunca se intentó publicar");
        var message = await SeedOutboxMessageAsync(provider, integrationEvent);

        await using var scope = provider.CreateAsyncScope();
        var reprocessor = scope.ServiceProvider.GetRequiredService<IDeadLetterReprocessor>();
        var result = await reprocessor.ReprocessAsync(new DeadLetterReprocessRequest(
            message.Id,
            new AuditActor("operador-test", AuditActorType.User),
            "Intento inválido -- la fila nunca se agotó."));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DeadLetter.NotExhausted");
        auditWriter.Entries.Should().BeEmpty("no debe auditarse ni mutarse nada si la fila no está agotada");
    }

    private static async Task<ConsumeResult<string, byte[]>?> ConsumeSingleMessageAsync(
        KafkaMessagingOptions options,
        string topic,
        TimeSpan overallTimeout)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(
            KafkaClientConfigFactory.BuildConsumerConfig(options, $"dlq-verify-{Guid.NewGuid():N}")).Build();
        consumer.Subscribe(topic);

        var deadline = DateTime.UtcNow.Add(overallTimeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(2));
                if (result is { IsPartitionEOF: false, Message: not null })
                {
                    return result;
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        return null;
    }

    private static string? GetHeaderString(Headers headers, string key) =>
        headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
}

/// <summary>Evento de integración de prueba dedicado a <see cref="OutboxDeadLetterIntegrationTests"/> (F3-08).</summary>
public sealed record OutboxDeadLetterTestEvent(Guid AccountId, string Name) : DomainEvent, IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Tests.OutboxDeadLetterTestEvent";

    public int SchemaVersion => 1;
}

/// <summary>
/// <see cref="IEventPublisher"/> de prueba cuyo comportamiento (fallar/tener éxito) se puede cambiar en
/// caliente entre ciclos de <see cref="OutboxBatchProcessor.ProcessBatchAsync"/> -- necesario para
/// simular, en el mismo test, "el mensaje agota reintentos" y luego "tras el reprocesamiento, la causa
/// raíz ya se corrigió y el evento se publica con éxito".
/// </summary>
public sealed class TogglableEventPublisher : IEventPublisher
{
    private readonly ConcurrentBag<Guid> _publishedEventIds = new();

    public bool ShouldFail { get; set; }

    public IReadOnlyCollection<Guid> PublishedEventIds => _publishedEventIds;

    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("Fallo simulado de publicación (F3-08, DLQ).");
        }

        _publishedEventIds.Add(integrationEvent.EventId);
        return Task.CompletedTask;
    }

    public async Task PublishAsync(IEnumerable<IIntegrationEvent> integrationEvents, CancellationToken cancellationToken = default)
    {
        foreach (var integrationEvent in integrationEvents)
        {
            await PublishAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
