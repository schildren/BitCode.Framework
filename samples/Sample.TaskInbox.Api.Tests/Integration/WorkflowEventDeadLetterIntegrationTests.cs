using System.Text;
using BitCode.Framework.Platform.TaskInbox;
using BitCode.Framework.Platform.TaskInbox.Bandeja;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;
using BitCode.Framework.Shared.Infrastructure.Persistence;
using BitCode.Framework.Shared.Testing;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.TaskInbox.Api.Tests.Integration;

/// <summary>
/// F9-04 (Plan Maestro, Fase 9: "Completar eventos, Inbox, Outbox y compensaciones" -- criterio de
/// aceptación "Flujo eventual probado"): a diferencia de <see cref="TaskInboxEndpointsIntegrationTests"/>
/// (que simula la entrega de un evento de Workflow invocando <c>IInboxMessageProcessor.ProcessAsync</c>
/// directamente, sin broker real) y de las pruebas genéricas de F3-07/F3-08/F3-09
/// (<c>KafkaEventConsumerPoisonMessageIntegrationTests</c>, <c>InboxConsumerIntegrationTests</c>, que usan
/// un evento de prueba sintético), esta suite ejercita el mecanismo de COMPENSACIÓN real (reintentos con
/// backoff agotados -> dead-letter, F3-07/F3-08) específicamente contra
/// <see cref="TareaAsignadaIntegrationEvent"/> -- el evento de integración real de Workflow que consume
/// Task Inbox -- contra un broker Kafka real y un SQL Server real (Testcontainers).
/// </summary>
/// <remarks>
/// <para>
/// <b>Hallazgo que motiva esta suite:</b> antes de F9-04, el mecanismo genérico de dead-letter
/// (<c>KafkaEventConsumer{TEvent}</c>, F3-07/F3-08) solo tenía prueba de integración real para el caso
/// "mensaje poison" (JSON corrupto, <c>KafkaEventConsumerPoisonMessageIntegrationTests</c>) -- NINGUNA
/// prueba en el repositorio ejercitaba el otro camino a dead-letter (F3-08): un mensaje que SÍ se
/// deserializa pero cuyo HANDLER DE NEGOCIO falla de forma permanente/agota sus reintentos
/// (<see cref="EventProcessingExhaustedException"/>). Esta suite cierra ese hueco, y lo hace
/// específicamente sobre el flujo Workflow -> TaskInbox que es el piloto de Fase 9 (ADR 0020), en vez de
/// sobre un evento de prueba genérico.
/// </para>
/// <para>
/// El <see cref="IEventConsumer{TEvent}"/> real de este flujo
/// (<c>TareaAsignadaIntegrationEventConsumer</c>) es <c>internal</c> a
/// <c>BitCode.Platform.TaskInbox</c> y no puede referenciarse desde este ensamblado de test. Esta suite
/// usa <see cref="SelectivelyFailingTareaAsignadaConsumer"/>, un doble de prueba que replica el mismo
/// efecto observable (alta/actualización de <see cref="TaskInboxItem"/> vía <see cref="IRepository{TEntity,TId}"/>)
/// pero que, para un <c>WorkflowTaskId</c> señalizado explícitamente por el test, simula el caso "una
/// regla de negocio rechaza el mensaje de forma permanente" -- el mismo tipo de fallo que un consumidor
/// real produciría ante un dato de negocio inválido que ningún reintento puede corregir.
/// </para>
/// </remarks>
public class WorkflowEventDeadLetterIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private readonly KafkaContainerFixture _kafkaFixture = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sqlServerFixture.InitializeAsync(), _kafkaFixture.InitializeAsync());
    }

    public async Task DisposeAsync()
    {
        await _sqlServerFixture.DisposeAsync();
        await _kafkaFixture.DisposeAsync();
    }

    private KafkaMessagingOptions BuildKafkaOptions(string consumerGroupId) => new()
    {
        BootstrapServers = _kafkaFixture.BootstrapServers,
        ClientId = "workflow-event-dlq-tests",
        ConsumerGroupId = consumerGroupId,
    };

    private async Task<ServiceProvider> BuildProviderAsync(string connectionString, Guid permanentlyFailingWorkflowTaskId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<TaskInboxDbContext>(connectionString);
        services.AddSharedApplication(typeof(WorkflowEventDeadLetterIntegrationTests).Assembly);
        services.AddSingleton(new PermanentFailureSimulationOptions(permanentlyFailingWorkflowTaskId));
        // Registrado explícitamente (no vía AddSharedTaskInbox, que registraría el consumidor real
        // internal con TryAddScoped): un scope nuevo por mensaje (KafkaEventConsumer<TEvent>) resuelve
        // una instancia nueva de este doble de prueba, con un IRepository<TaskInboxItem,Guid> propio de
        // ese scope -- mismo ciclo de vida documentado en docs/guia-inbox-consumer.md.
        services.AddScoped<IEventConsumer<TareaAsignadaIntegrationEvent>, SelectivelyFailingTareaAsignadaConsumer>();

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TaskInboxDbContext>().Database.EnsureCreatedAsync();

        return provider;
    }

    /// <summary>
    /// Criterio de aceptación: un evento real de Workflow (<see cref="TareaAsignadaIntegrationEvent"/>)
    /// cuyo handler de negocio falla de forma permanente se aísla a un tópico dead-letter real (F3-08)
    /// en vez de bloquear la partición o reintentarse para siempre -- y el mensaje válido siguiente de la
    /// misma partición se sigue procesando con normalidad (el read-model de Task Inbox se actualiza).
    /// </summary>
    [Fact]
    public async Task TareaAsignada_HandlerFallaPermanentemente_SeAislaADeadLetterYElConsumerSigueOperando()
    {
        var connectionString = _sqlServerFixture.BuildIsolatedConnectionString("WorkflowEventDlq", Guid.NewGuid().ToString("N"));

        var poisonedWorkflowTaskId = Guid.NewGuid();
        var poisonedEvent = new TareaAsignadaIntegrationEvent(poisonedWorkflowTaskId, Guid.NewGuid(), Guid.NewGuid());

        var validWorkflowTaskId = Guid.NewGuid();
        var validAsignadoUserId = Guid.NewGuid();
        var validEvent = new TareaAsignadaIntegrationEvent(validWorkflowTaskId, Guid.NewGuid(), validAsignadoUserId);

        var kafkaOptions = BuildKafkaOptions($"grupo-{Guid.NewGuid():N}");

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(kafkaOptions)).Build();
        var publisher = new KafkaEventPublisher(producer);

        // Mismo tópico ("Workflow.TareaAsignada", EventType fijo del contrato real -- no parametrizable
        // por test como los eventos sintéticos de otras suites): aislado igual, porque este broker
        // Kafka es una instancia de Testcontainers propia de ESTA clase de test, nunca compartida.
        await publisher.PublishAsync(poisonedEvent);
        await publisher.PublishAsync(validEvent);

        await using var provider = await BuildProviderAsync(connectionString, poisonedWorkflowTaskId);

        // MaxAttempts=1 + BaseDelay=Zero: agota el margen de reintentos en el primer intento, sin
        // esperar ningún backoff -- el criterio de aceptación de esta prueba es el mecanismo de
        // compensación (dead-letter), no el cronograma exacto de reintentos (ya cubierto por
        // KafkaEventPublishFailureClassifierTests/EventRetryBackoff).
        var retryOptions = new EventRetryPolicyOptions { MaxAttempts = 1, BaseDelay = TimeSpan.Zero };
        var deadLetterPublisher = new KafkaDeadLetterPublisher(producer);

        using var consumer = new KafkaEventConsumer<TareaAsignadaIntegrationEvent>(
            kafkaOptions,
            poisonedEvent.EventType,
            provider.GetRequiredService<IServiceScopeFactory>(),
            retryOptions: retryOptions,
            deadLetterPublisher: deadLetterPublisher);

        // Primera entrega (mensaje "poisoned" a nivel de negocio): debe agotar su único intento y
        // lanzar EventProcessingExhaustedException -- el offset se confirma igual (ver remarks de
        // KafkaEventConsumer<TEvent>), así que la partición no queda bloqueada.
        var exhausted = await PollUntilExhaustedOrTimeoutAsync(consumer, TimeSpan.FromSeconds(30));
        exhausted.Should().NotBeNull("el handler de negocio debe agotar sus reintentos y señalizar el agotamiento");
        exhausted!.EventType.Should().Be(poisonedEvent.EventType);
        exhausted.MessageId.Should().Be(poisonedEvent.EventId.ToString());

        // Segunda entrega (mensaje válido siguiente): el consumer debe seguir operando con normalidad.
        var secondCallProcessed = await PollUntilTrueOrTimeoutAsync(consumer, TimeSpan.FromSeconds(30));
        secondCallProcessed.Should().BeTrue("el consumer debe seguir procesando mensajes siguientes tras aislar el anterior a dead-letter");

        // El mensaje agotado debe haber llegado al tópico dead-letter con los metadatos esperados.
        var dlqTopic = DefaultKafkaTopicNameResolver.Instance.ResolveTopicName(poisonedEvent.EventType) + KafkaDeadLetterPublisher.DeadLetterTopicSuffix;
        using var dlqConsumer = new ConsumerBuilder<string, byte[]>(
            KafkaClientConfigFactory.BuildConsumerConfig(kafkaOptions, $"dlq-verify-{Guid.NewGuid():N}")).Build();
        dlqConsumer.Subscribe(dlqTopic);
        var dlqResult = await PollUntilConsumedAsync(dlqConsumer, TimeSpan.FromSeconds(30));

        dlqResult.Should().NotBeNull("el mensaje agotado debe haberse publicado al tópico dead-letter");
        GetHeaderString(dlqResult!.Message.Headers, KafkaDeadLetterPublisher.SourceMessageIdHeader).Should().Be(poisonedEvent.EventId.ToString());
        GetHeaderString(dlqResult.Message.Headers, KafkaDeadLetterPublisher.AttemptsHeader).Should().Be("1");
        GetHeaderString(dlqResult.Message.Headers, KafkaDeadLetterPublisher.ReasonHeader)
            .Should().Contain("regla de negocio permanente", "el motivo debe distinguir este agotamiento de un poison message (F3-09)");

        // Efecto de negocio: la tarea "poisoned" nunca debe haber creado una fila (su handler siempre
        // lanzó antes de llegar a persistir nada); la tarea válida sí debe reflejarse en el read-model.
        await using var verificationScope = provider.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<TaskInboxDbContext>();

        var poisonedItem = await context.TaskInboxItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == poisonedWorkflowTaskId);
        poisonedItem.Should().BeNull("el handler de la tarea agotada nunca debe haber persistido ningún efecto de negocio");

        var validItem = await context.TaskInboxItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == validWorkflowTaskId);
        validItem.Should().NotBeNull("el mensaje válido siguiente sí debe reflejarse en el read-model de Task Inbox");
        validItem!.AsignadoAUserId.Should().Be(validAsignadoUserId);
    }

    private static async Task<EventProcessingExhaustedException?> PollUntilExhaustedOrTimeoutAsync(
        KafkaEventConsumer<TareaAsignadaIntegrationEvent> consumer, TimeSpan overallTimeout)
    {
        var deadline = DateTime.UtcNow.Add(overallTimeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(2));
            }
            catch (EventProcessingExhaustedException exhausted)
            {
                return exhausted;
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        return null;
    }

    private static async Task<bool> PollUntilTrueOrTimeoutAsync(
        KafkaEventConsumer<TareaAsignadaIntegrationEvent> consumer, TimeSpan overallTimeout)
    {
        var deadline = DateTime.UtcNow.Add(overallTimeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(2)))
                {
                    return true;
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        return false;
    }

    private static async Task<ConsumeResult<string, byte[]>?> PollUntilConsumedAsync(IConsumer<string, byte[]> consumer, TimeSpan overallTimeout)
    {
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

/// <summary>Señala, a <see cref="SelectivelyFailingTareaAsignadaConsumer"/>, qué <c>WorkflowTaskId</c>
/// concreto debe simular como rechazado por una regla de negocio permanente en esta ejecución de test.</summary>
internal sealed record PermanentFailureSimulationOptions(Guid PermanentlyFailingWorkflowTaskId);

/// <summary>
/// Doble de prueba de <c>TareaAsignadaIntegrationEventConsumer</c> (internal a
/// <c>BitCode.Platform.TaskInbox</c>, no referenciable desde este ensamblado): replica el mismo efecto
/// observable (alta/actualización de <see cref="TaskInboxItem"/>) para cualquier
/// <see cref="TareaAsignadaIntegrationEvent"/> normal, pero simula una regla de negocio que rechaza de
/// forma PERMANENTE al <c>WorkflowTaskId</c> señalizado por <see cref="PermanentFailureSimulationOptions"/>
/// -- nunca llega a tocar el repositorio para ese caso, igual que haría un handler real que detecta un
/// dato de negocio inválido antes de cualquier escritura.
/// </summary>
internal sealed class SelectivelyFailingTareaAsignadaConsumer(
    IRepository<TaskInboxItem, Guid> repository, PermanentFailureSimulationOptions options)
    : IEventConsumer<TareaAsignadaIntegrationEvent>
{
    public async Task ConsumeAsync(TareaAsignadaIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (integrationEvent.WorkflowTaskId == options.PermanentlyFailingWorkflowTaskId)
        {
            throw new InvalidOperationException(
                $"Simulación F9-04: la tarea {integrationEvent.WorkflowTaskId} viola una regla de negocio permanente (nunca debe reintentarse).");
        }

        var item = await repository.GetByIdAsync(integrationEvent.WorkflowTaskId, cancellationToken);
        if (item is null)
        {
            await repository.AddAsync(
                new TaskInboxItem(
                    integrationEvent.WorkflowTaskId, integrationEvent.WorkflowInstanceId,
                    integrationEvent.AsignadoAUserId, DateTime.UtcNow),
                cancellationToken);
            return;
        }

        item.AplicarAsignacion(integrationEvent.AsignadoAUserId, DateTime.UtcNow);
        repository.Update(item);
    }
}
