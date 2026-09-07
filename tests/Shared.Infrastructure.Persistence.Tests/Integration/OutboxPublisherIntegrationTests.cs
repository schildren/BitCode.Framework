using System.Collections.Concurrent;
using System.Text.Json;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Application.Inbox;
using BitCode.Framework.Shared.Domain.Outbox;
using BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;
using BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;
using BitCode.Framework.Shared.Kernel;
using BitCode.Framework.Shared.Testing;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F3-03 (Outbox Publisher): criterio de aceptación literal "reinicio no pierde eventos", verificado
/// contra SQL Server real (donde vive <see cref="OutboxMessage"/>) y, para el flujo de punta a punta,
/// contra un broker Kafka real (<see cref="KafkaContainerFixture"/>, mismo fixture de F3-02).
/// </summary>
[Collection(OutboxPublisherCollection.Name)]
public class OutboxPublisherIntegrationTests(SqlServerContainerFixture sqlFixture, KafkaContainerFixture kafkaFixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        sqlFixture.BuildIsolatedConnectionString("OutboxPublisher", testName);

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString, IEventPublisher eventPublisher, OutboxPublisherOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        services.AddSingleton(eventPublisher);
        services.AddSingleton(options ?? new OutboxPublisherOptions());
        // F3-07: sin AddSharedOutboxPublisher/AddSharedMessagingKafka, OutboxBatchProcessor necesita su
        // clasificador de fallos registrado explícitamente — DefaultEventPublishFailureClassifier
        // (trata todo como transitorio) alcanza para estos tests, que no ejercitan la clasificación
        // específica de Kafka (ver KafkaEventPublishFailureClassifierTests para eso).
        services.AddSingleton<IEventPublishFailureClassifier, DefaultEventPublishFailureClassifier>();
        // Registro manual (sin AddSharedOutboxPublisher): estos tests llaman OutboxBatchProcessor
        // directamente, ciclo por ciclo, en vez de dejar correr el loop de
        // OutboxPublisherBackgroundService — necesitan controlar exactamente cuándo ocurre cada
        // "ciclo de sondeo" para simular caídas del proceso entre pasos.
        services.AddScoped<OutboxBatchProcessor>();

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    private static async Task<OutboxMessage> SeedOutboxMessageAsync(
        IServiceProvider provider,
        object domainEvent,
        DateTime? lockedUntilUtc = null,
        string? lockedBy = null)
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
            LockedUntilUtc = lockedUntilUtc,
            LockedBy = lockedBy,
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

    /// <summary>
    /// Criterio de aceptación literal (parte 1, "reinicio no pierde eventos" — nunca perdió el evento
    /// porque nunca llegó a intentarlo): un fallo/caída ANTES de publicar deja la fila pendiente y sin
    /// bloqueo vigente; un ciclo posterior (el "reinicio") la reclama y publica con normalidad.
    /// </summary>
    [Fact]
    public async Task ProcessBatchAsync_MessageNeverAttempted_IsClaimedAndPublishedOnNextCycle()
    {
        var publisher = new RecordingEventPublisher();
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString(), publisher);
        var integrationEvent = new OutboxPublisherTestEvent(Guid.NewGuid(), "Antes de publicar");
        var message = await SeedOutboxMessageAsync(provider, integrationEvent);

        // "Reinicio": un ciclo de ProcessBatchAsync que nunca corrió antes sobre esta fila.
        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var result = await processor.ProcessBatchAsync();
            result.Claimed.Should().Be(1);
            result.Published.Should().Be(1);
        }

        publisher.PublishedEventIds.Should().ContainSingle().Which.Should().Be(integrationEvent.EventId);

        var persisted = await FindOutboxMessageAsync(provider, message.Id);
        persisted!.ProcessedAtUtc.Should().NotBeNull();
        persisted.LockedUntilUtc.Should().BeNull();
    }

    /// <summary>
    /// Criterio de aceptación literal (parte 2): si la publicación de una fila falla (equivalente,
    /// para este test, a una caída del proceso a mitad del intento), la fila NO se marca como
    /// procesada y queda lista para reintento en un ciclo posterior — F3-07: con
    /// <see cref="EventRetryPolicyOptions.BaseDelay"/> en cero (configurado explícitamente en este
    /// test), el backoff calculado es <see cref="TimeSpan.Zero"/>, así que sigue pudiendo reclamarse de
    /// inmediato en el siguiente ciclo (mismo comportamiento observable que antes de F3-07, sin
    /// backoff real de por medio) — <c>OutboxPublisherRetryTests</c> cubre el caso con backoff/límite
    /// real.
    /// </summary>
    [Fact]
    public async Task ProcessBatchAsync_PublishFails_LeavesMessageUnprocessedAndRetriesNextCycle()
    {
        var failingPublisher = new RecordingEventPublisher(shouldThrowOnFirstCall: true);
        var options = new OutboxPublisherOptions { Retry = new EventRetryPolicyOptions { BaseDelay = TimeSpan.Zero } };
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString(), failingPublisher, options);
        var integrationEvent = new OutboxPublisherTestEvent(Guid.NewGuid(), "Falla la primera vez");
        var message = await SeedOutboxMessageAsync(provider, integrationEvent);

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var firstAttempt = await processor.ProcessBatchAsync();
            firstAttempt.Claimed.Should().Be(1);
            firstAttempt.Failed.Should().Be(1);
            firstAttempt.Published.Should().Be(0);
            firstAttempt.Exhausted.Should().Be(0, "todavía queda margen de reintentos (MaxAttempts por defecto = 10)");
        }

        var afterFirstAttempt = await FindOutboxMessageAsync(provider, message.Id);
        afterFirstAttempt!.ProcessedAtUtc.Should().BeNull("un fallo de publicación nunca debe marcar la fila como procesada");
        afterFirstAttempt.RetryCount.Should().Be(1);
        afterFirstAttempt.ExhaustedAtUtc.Should().BeNull("todavía queda margen de reintentos");
        afterFirstAttempt.LockedUntilUtc.Should().NotBeNull("F3-07: el próximo intento se programa como una marca de tiempo futura (backoff), no más como lock liberado a null");

        // "Reinicio" (ciclo posterior): esta vez el publisher no falla. Con BaseDelay = Zero el backoff
        // calculado es Zero, así que ya puede reclamarse de nuevo sin esperar nada adicional.
        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var secondAttempt = await processor.ProcessBatchAsync();
            secondAttempt.Claimed.Should().Be(1);
            secondAttempt.Published.Should().Be(1);
        }

        var afterSecondAttempt = await FindOutboxMessageAsync(provider, message.Id);
        afterSecondAttempt!.ProcessedAtUtc.Should().NotBeNull();
        failingPublisher.PublishedEventIds.Should().ContainSingle().Which.Should().Be(integrationEvent.EventId);
    }

    /// <summary>
    /// Mapeo OutboxMessage -> IIntegrationEvent (la decisión de diseño de F3-03): un DomainEvent que
    /// NUNCA implementó IIntegrationEvent (como <see cref="TestAccountOpenedEvent"/>, F1-23) es interno
    /// al bounded context — el relay lo marca como procesado (ya fue "considerado") pero jamás llama a
    /// IEventPublisher para él.
    /// </summary>
    [Fact]
    public async Task ProcessBatchAsync_InternalDomainEvent_MarksProcessedWithoutPublishing()
    {
        var publisher = new RecordingEventPublisher();
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString(), publisher);
        var internalDomainEvent = new TestAccountOpenedEvent(Guid.NewGuid(), "Cuenta interna");
        var message = await SeedOutboxMessageAsync(provider, internalDomainEvent);

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var result = await processor.ProcessBatchAsync();
            result.Claimed.Should().Be(1);
            result.SkippedInternal.Should().Be(1);
            result.Published.Should().Be(0);
        }

        publisher.PublishedEventIds.Should().BeEmpty("un DomainEvent puramente interno nunca debe llegar a IEventPublisher");

        var persisted = await FindOutboxMessageAsync(provider, message.Id);
        persisted!.ProcessedAtUtc.Should().NotBeNull("la fila queda considerada/cerrada aunque no haya nada que publicar");
    }

    /// <summary>
    /// Criterio de aceptación literal (parte 3, el caso más delicado): si el proceso muere DESPUÉS de
    /// que el evento ya llegó al broker pero ANTES de marcar la fila como procesada, el reinicio vuelve
    /// a publicarlo (duplicado aceptable, at-least-once) — nunca deja la fila huérfana sin publicar.
    /// Simula la caída publicando manualmente el evento al broker real (representa la publicación que
    /// ya ocurrió antes de la caída) sin marcar la fila, y luego corre un ciclo normal de
    /// ProcessBatchAsync (el "reinicio") que la reclama de nuevo.
    /// </summary>
    [Fact]
    public async Task ProcessBatchAsync_CrashAfterPublish_RestartRepublishesAndMarks()
    {
        var kafkaOptions = new KafkaMessagingOptions
        {
            BootstrapServers = kafkaFixture.BootstrapServers,
            ClientId = "outbox-publisher-tests",
            ConsumerGroupId = $"grupo-{Guid.NewGuid():N}",
        };

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(kafkaOptions)).Build();
        var kafkaPublisher = new KafkaEventPublisher(producer);

        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString(), kafkaPublisher);
        var integrationEvent = new OutboxPublisherTestEvent(Guid.NewGuid(), "Publicado antes de la caída simulada");
        var message = await SeedOutboxMessageAsync(provider, integrationEvent);

        // Simula la publicación que ya había ocurrido antes de que el proceso "muriera": el evento
        // llega al broker real, pero la fila de Outbox nunca se marca (el SaveChangesAsync que la
        // hubiera marcado nunca llegó a ejecutarse).
        await kafkaPublisher.PublishAsync(integrationEvent);

        var beforeRestart = await FindOutboxMessageAsync(provider, message.Id);
        beforeRestart!.ProcessedAtUtc.Should().BeNull("simula que el proceso murió antes de poder marcar la fila");

        // "Reinicio": un ciclo normal de ProcessBatchAsync reclama la fila todavía pendiente y la
        // publica de nuevo (duplicado aceptable), esta vez marcándola con éxito.
        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var result = await processor.ProcessBatchAsync();
            result.Claimed.Should().Be(1);
            result.Published.Should().Be(1);
        }

        var afterRestart = await FindOutboxMessageAsync(provider, message.Id);
        afterRestart!.ProcessedAtUtc.Should().NotBeNull("el reinicio debe terminar dejando la fila marcada, nunca huérfana");

        // Verifica contra el broker real que el evento efectivamente llegó dos veces (duplicado
        // aceptable, semántica at-least-once) — ninguna de las dos entregas se perdió.
        // F3-04: KafkaEventConsumer<TEvent> ahora resuelve IInboxMessageProcessor/IEventConsumer<TEvent>
        // de un scope de DI por mensaje; para esta verificación puramente de round-trip contra el
        // broker (el punto de este test es contar cuántas veces llegó el evento, no ejercitar Inbox)
        // alcanza con un InboxMessageProcessor "passthrough" que nunca deduplica.
        var handler = new RecordingEventConsumer<OutboxPublisherTestEvent>();
        var services = new ServiceCollection();
        services.AddSingleton<IInboxMessageProcessor>(new PassthroughInboxMessageProcessor());
        services.AddSingleton<IEventConsumer<OutboxPublisherTestEvent>>(handler);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        using var consumer = new KafkaEventConsumer<OutboxPublisherTestEvent>(kafkaOptions, integrationEvent.EventType, scopeFactory);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (handler.ReceivedEvents.Count < 2 && DateTime.UtcNow < deadline)
        {
            try
            {
                await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(2));
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        handler.ReceivedEvents.Should().HaveCount(2, "el duplicado es aceptable (at-least-once); lo inaceptable sería CERO entregas");
        handler.ReceivedEvents.Select(e => e.EventId).Should().AllBeEquivalentTo(integrationEvent.EventId);
    }

    /// <summary>
    /// Bloqueo (evitar publicación duplicada entre réplicas del worker): dos instancias de
    /// <see cref="OutboxBatchProcessor"/> (cada una con su propio <c>DbContext</c>/scope, simulando dos
    /// réplicas del proceso) que sondean el mismo backlog concurrentemente nunca publican la misma fila
    /// dos veces — el total publicado debe ser exactamente igual al total de filas sembradas, sin
    /// duplicados.
    /// </summary>
    [Fact]
    public async Task ProcessBatchAsync_TwoConcurrentInstances_NeverPublishSameMessageTwice()
    {
        const int messageCount = 40;
        var publisher = new RecordingEventPublisher();
        var options = new OutboxPublisherOptions { BatchSize = 5 };
        var connectionString = BuildIsolatedConnectionString();
        await using var provider = await BuildProviderAsync(connectionString, publisher, options);

        var seededEventIds = new List<Guid>(messageCount);
        for (var i = 0; i < messageCount; i++)
        {
            var integrationEvent = new OutboxPublisherTestEvent(Guid.NewGuid(), $"Mensaje {i}");
            await SeedOutboxMessageAsync(provider, integrationEvent);
            seededEventIds.Add(integrationEvent.EventId);
        }

        async Task DrainAsync()
        {
            while (true)
            {
                await using var scope = provider.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
                var result = await processor.ProcessBatchAsync();
                if (result.Claimed == 0)
                {
                    return;
                }
            }
        }

        // Dos "réplicas" del worker drenando el mismo backlog al mismo tiempo, cada una con su propio
        // scope/DbContext (mismo patrón que dos procesos independientes contra la misma base).
        await Task.WhenAll(DrainAsync(), DrainAsync());

        publisher.PublishedEventIds.Should().HaveCount(messageCount, "cada mensaje debe publicarse exactamente una vez, sin duplicados entre instancias");
        publisher.PublishedEventIds.Distinct().Should().HaveCount(messageCount);
        publisher.PublishedEventIds.Should().BeEquivalentTo(seededEventIds);
    }
}

/// <summary>Evento de integración de prueba (implementa DomainEvent + IIntegrationEvent) dedicado a ejercitar el relay de Outbox (F3-03).</summary>
public sealed record OutboxPublisherTestEvent(Guid AccountId, string Name) : DomainEvent, IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Tests.OutboxPublisherTestEvent";

    public int SchemaVersion => 1;
}

/// <summary>
/// <see cref="IEventPublisher"/> de prueba que registra los <see cref="IIntegrationEvent.EventId"/>
/// publicados (thread-safe, para el test de dos instancias concurrentes) y opcionalmente falla en la
/// primera llamada (para simular un fallo de publicación).
/// </summary>
public sealed class RecordingEventPublisher(bool shouldThrowOnFirstCall = false) : IEventPublisher
{
    private int _callCount;
    private readonly ConcurrentBag<Guid> _publishedEventIds = new();

    public IReadOnlyCollection<Guid> PublishedEventIds => _publishedEventIds;

    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var callNumber = Interlocked.Increment(ref _callCount);
        if (shouldThrowOnFirstCall && callNumber == 1)
        {
            throw new InvalidOperationException("Fallo simulado de publicación (primera llamada).");
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

/// <summary>Handler de prueba que registra cada evento recibido, para verificar contra Kafka real.</summary>
public sealed class RecordingEventConsumer<TEvent> : IEventConsumer<TEvent>
    where TEvent : IIntegrationEvent
{
    private readonly ConcurrentBag<TEvent> _receivedEvents = new();

    public IReadOnlyCollection<TEvent> ReceivedEvents => _receivedEvents;

    public Task ConsumeAsync(TEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _receivedEvents.Add(integrationEvent);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Test double de <see cref="IInboxMessageProcessor"/> que nunca deduplica (siempre ejecuta
/// <c>handler</c>): usado únicamente por <see cref="OutboxPublisherIntegrationTests"/>, cuyo interés es
/// contar cuántas veces el broker entrega un evento (duplicado aceptable del relay de Outbox, F3-03),
/// no ejercitar la deduplicación real de Inbox (F3-04, ver <c>InboxConsumerIntegrationTests</c>).
/// </summary>
public sealed class PassthroughInboxMessageProcessor : IInboxMessageProcessor
{
    public async Task<InboxProcessOutcome> ProcessAsync(
        string messageId,
        string messageType,
        string payload,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        await handler(cancellationToken).ConfigureAwait(false);
        return InboxProcessOutcome.Processed;
    }
}
