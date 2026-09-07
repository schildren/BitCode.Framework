using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Inbox;
using BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;
using BitCode.Framework.Shared.Testing;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F3-04 (Inbox Consumer): verifica, contra un SQL Server real (donde vive <see cref="InboxMessage"/>,
/// F1-24) y un broker Kafka real (F3-02), que <see cref="KafkaEventConsumer{TEvent}"/> coordina con
/// <c>IInboxMessageProcessor</c> (F1-24) de forma que un mensaje reentregado por Kafka nunca vuelve a
/// ejecutar el efecto de negocio (<see cref="IEventConsumer{TEvent}.ConsumeAsync"/>) — criterio de
/// aceptación literal de F3-04: "duplicados no repiten efectos".
/// </summary>
[Collection(InboxConsumerCollection.Name)]
public class InboxConsumerIntegrationTests(SqlServerContainerFixture sqlFixture, KafkaContainerFixture kafkaFixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        sqlFixture.BuildIsolatedConnectionString("InboxConsumer", testName);

    private KafkaMessagingOptions BuildKafkaOptions(string consumerGroupId) => new()
    {
        BootstrapServers = kafkaFixture.BootstrapServers,
        ClientId = "inbox-consumer-tests",
        ConsumerGroupId = consumerGroupId,
    };

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString, RecordingEventConsumer<InboxConsumerTestEvent> handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        services.AddSharedApplication(typeof(InboxConsumerIntegrationTests).Assembly);
        // F3-04: el handler de negocio se resuelve del mismo scope de DI que IInboxMessageProcessor
        // (ver remarks de KafkaEventConsumer<TEvent>) — este handler de prueba no toca ningún
        // DbContext, así que una única instancia compartida (Singleton) alcanza para contar
        // ejecuciones entre los distintos scopes que KafkaEventConsumer crea por mensaje.
        services.AddSingleton<IEventConsumer<InboxConsumerTestEvent>>(handler);

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    /// <summary>
    /// Criterio de aceptación literal: publicar el mismo <see cref="IIntegrationEvent.EventId"/> dos
    /// veces a un tópico Kafka real y consumirlo dos veces con <see cref="KafkaEventConsumer{TEvent}"/>
    /// ejecuta el efecto de negocio (<see cref="IEventConsumer{TEvent}.ConsumeAsync"/>) UNA sola vez —
    /// la segunda entrega la descarta <c>IInboxMessageProcessor</c> por encontrar ya una fila
    /// <c>InboxMessage</c> con <c>ProcessedAtUtc</c> no nulo para el mismo <c>EventId</c>.
    /// </summary>
    [Fact]
    public async Task ConsumeAndHandleOnceAsync_SameEventPublishedTwice_ExecutesHandlerOnlyOnce()
    {
        var connectionString = BuildIsolatedConnectionString();
        var handler = new RecordingEventConsumer<InboxConsumerTestEvent>();
        await using var provider = await BuildProviderAsync(connectionString, handler);

        var kafkaOptions = BuildKafkaOptions($"grupo-{Guid.NewGuid():N}");
        var integrationEvent = new InboxConsumerTestEvent(Guid.NewGuid(), "Cuenta duplicada", $"{Guid.NewGuid():N}");

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(kafkaOptions)).Build();
        var publisher = new KafkaEventPublisher(producer);

        // Publica el MISMO evento (mismo EventId) dos veces: representa una reentrega de Kafka
        // (rebalance, reinicio del consumidor, o el duplicado aceptable del relay de Outbox, F3-03).
        await publisher.PublishAsync(integrationEvent);
        await publisher.PublishAsync(integrationEvent);

        using var consumer = new KafkaEventConsumer<InboxConsumerTestEvent>(
            kafkaOptions,
            integrationEvent.EventType,
            provider.GetRequiredService<IServiceScopeFactory>());

        var deadline = DateTime.UtcNow.AddSeconds(30);
        var messagesConsumed = 0;
        while (messagesConsumed < 2 && DateTime.UtcNow < deadline)
        {
            try
            {
                if (await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(2)))
                {
                    messagesConsumed++;
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        messagesConsumed.Should().Be(2, "el broker debe haber entregado ambas copias del mensaje");
        handler.ReceivedEvents.Should().ContainSingle(
            "el efecto de negocio no debe repetirse aunque Kafka entregue el mensaje dos veces (duplicados no repiten efectos)");

        await using var verificationScope = provider.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var inboxMessages = await context.Set<InboxMessage>().IgnoreQueryFilters()
            .Where(message => message.MessageId == integrationEvent.EventId.ToString())
            .ToListAsync();
        inboxMessages.Should().HaveCount(1);
        inboxMessages[0].ProcessedAtUtc.Should().NotBeNull();
    }

    /// <summary>
    /// Simula que el proceso "murió" DESPUÉS de que <c>IInboxMessageProcessor.ProcessAsync</c> ya
    /// persistió la fila de Inbox como procesada (el handler de negocio ya corrió con éxito) pero ANTES
    /// de que <see cref="KafkaEventConsumer{TEvent}"/> alcanzara a confirmar el offset ante Kafka:
    /// sembrando directamente la fila <c>InboxMessage</c> ya marcada como procesada para el mismo
    /// <c>EventId</c> ANTES de invocar <see cref="KafkaEventConsumer{TEvent}.ConsumeAndHandleOnceAsync"/>,
    /// se reproduce exactamente lo que Kafka reentrega al reiniciar (el mismo mensaje, offset no
    /// confirmado). El handler de negocio NO debe volver a ejecutarse (criterio de aceptación literal
    /// de F3-04), aunque el offset recién se confirme en ESTE intento.
    /// </summary>
    [Fact]
    public async Task ConsumeOnce_MessageAlreadyProcessed_SkipsHandlerButCommitsOffset()
    {
        var connectionString = BuildIsolatedConnectionString();
        var handler = new RecordingEventConsumer<InboxConsumerTestEvent>();
        await using var provider = await BuildProviderAsync(connectionString, handler);

        var kafkaOptions = BuildKafkaOptions($"grupo-{Guid.NewGuid():N}");
        var integrationEvent = new InboxConsumerTestEvent(Guid.NewGuid(), "Cuenta ya procesada antes de la caída", $"{Guid.NewGuid():N}");

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(kafkaOptions)).Build();
        var publisher = new KafkaEventPublisher(producer);
        await publisher.PublishAsync(integrationEvent);

        // Simula el efecto de negocio ya persistido por un intento anterior que murió antes de
        // confirmar el offset: la fila de Inbox ya existe con ProcessedAtUtc no nulo para este EventId.
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
            context.Set<InboxMessage>().Add(new InboxMessage
            {
                Id = Guid.NewGuid(),
                MessageId = integrationEvent.EventId.ToString(),
                MessageType = integrationEvent.EventType,
                PayloadJson = "{}",
                ReceivedAtUtc = DateTime.UtcNow,
                ProcessedAtUtc = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        using var consumer = new KafkaEventConsumer<InboxConsumerTestEvent>(
            kafkaOptions,
            integrationEvent.EventType,
            provider.GetRequiredService<IServiceScopeFactory>());

        var deadline = DateTime.UtcNow.AddSeconds(30);
        var consumed = false;
        while (!consumed && DateTime.UtcNow < deadline)
        {
            try
            {
                consumed = await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(2));
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        consumed.Should().BeTrue("el mensaje reentregado se procesa (aunque sea descartado como duplicado) y el offset se confirma recién en este intento");
        handler.ReceivedEvents.Should().BeEmpty(
            "el handler ya había corrido en el intento anterior (simulado); Inbox lo descarta como duplicado sin volver a ejecutarlo");

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var inboxMessages = await verificationContext.Set<InboxMessage>().IgnoreQueryFilters()
            .Where(message => message.MessageId == integrationEvent.EventId.ToString())
            .ToListAsync();
        inboxMessages.Should().HaveCount(1, "no debe crearse una segunda fila para el mismo EventId");
    }
}

/// <summary>
/// Evento de integración de prueba dedicado a F3-04 (Inbox Consumer). <see cref="EventType"/> incluye
/// <see cref="Topic"/> (un valor único por test, mismo criterio que <c>TestOrderCreatedIntegrationEvent</c>
/// de <c>Shared.Infrastructure.Messaging.Kafka.Tests</c>): el tópico Kafka se deriva de <c>EventType</c>
/// (F3-02) y el <see cref="KafkaContainerFixture"/> es compartido entre TODOS los tests de esta clase
/// (misma colección) — sin este discriminador, dos tests que publican a un tópico con el mismo
/// <c>EventType</c> literal compartirían el mismo tópico Kafka y, con <c>AutoOffsetReset.Earliest</c>,
/// el consumidor (grupo nuevo) de un test leería también los mensajes publicados por otro test anterior.
/// </summary>
public sealed record InboxConsumerTestEvent(Guid AccountId, string Name, string Topic) : IntegrationEvent
{
    public override string EventType => $"Tests.InboxConsumerTestEvent.{Topic}";
}
