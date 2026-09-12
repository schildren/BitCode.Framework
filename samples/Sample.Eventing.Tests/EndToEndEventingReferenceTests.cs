using System.Collections.Concurrent;
using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;
using BitCode.Framework.Shared.Infrastructure.Persistence;
using BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;
using BitCode.Framework.Shared.Testing;
using Confluent.Kafka;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sample.Eventing.Facturacion;
using Sample.Eventing.Pedidos;

namespace Sample.Eventing.Tests;

/// <summary>
/// F3-13 (Fase 3, "Prueba de referencia"): ejemplo ejecutable del flujo COMPLETO entre dos módulos
/// (bounded contexts) usando TODA la infraestructura ya construida en F3-01 a F3-12 junta, en un
/// escenario de negocio realista — a diferencia de los tests aislados de cada tarea (que ejercitan una
/// pieza a la vez, con eventos de test "sueltos" o filas de Outbox sembradas a mano), este test:
///
/// <list type="number">
/// <item>Ejecuta <see cref="ConfirmarPedidoCommand"/> vía MediatR/<see cref="ISender"/> real (pipeline
/// completo: ValidationBehavior, LoggingBehavior, TransactionBehavior) contra <see cref="Pedido"/>
/// (Módulo A) — <see cref="Pedido.Confirmar"/> levanta <see cref="PedidoConfirmadoIntegrationEvent"/>
/// (DomainEvent real, F1-23) desde un agregado real.</item>
/// <item>Deja correr el <c>OutboxPublisherBackgroundService</c> REAL (F3-03), no invocado ciclo por
/// ciclo a mano, para publicar la fila de Outbox a Kafka.</item>
/// <item><c>KafkaEventConsumer&lt;PedidoConfirmadoIntegrationEvent&gt;</c> (F3-02/F3-04/F3-09) del
/// Módulo B (Facturación) consume el evento coordinado con Inbox real (SQL Server) y persiste su PROPIA
/// entidad (<see cref="FacturaPendiente"/>) — el efecto de negocio verificable de la consistencia
/// eventual entre ambos módulos.</item>
/// <item>Además del caso feliz, reutiliza la garantía de Inbox (F3-04, "duplicados no repiten
/// efectos") EN ESTE MISMO escenario de punta a punta: el mismo evento entregado una segunda vez no
/// duplica la <see cref="FacturaPendiente"/> creada.</item>
/// </list>
///
/// Toda la espera de consistencia eventual usa polling acotado con timeout (nunca un
/// <c>Thread.Sleep</c> fijo): la latencia real de publicación/consumo asíncrono contra un broker/SQL
/// Server real no es determinística.
/// </summary>
[Collection(SampleEventingCollection.Name)]
public class EndToEndEventingReferenceTests(SqlServerContainerFixture sqlFixture, KafkaContainerFixture kafkaFixture)
{
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    // F3-13: nombre corto explícito (en vez de [CallerMemberName], como en los demás tests de
    // integración de este repositorio) porque el nombre de este método de test es intencionalmente
    // descriptivo/largo — SqlServerContainerFixtureExtensions.BuildIsolatedConnectionString concatena
    // "{prefix}_{testName}_{Guid:N}" como Initial Catalog, y SqlConnectionStringBuilder rechaza un
    // valor de más de 128 caracteres.
    private string BuildIsolatedConnectionString() =>
        sqlFixture.BuildIsolatedConnectionString("SampleEventing", "E2E");

    private KafkaMessagingOptions BuildKafkaOptions() => new()
    {
        BootstrapServers = kafkaFixture.BootstrapServers,
        ClientId = "sample-eventing-reference-test",
        ConsumerGroupId = $"grupo-{Guid.NewGuid():N}",
    };

    /// <summary>
    /// Arma el contenedor de DI de UN ÚNICO proceso host de referencia (en un sistema real, Módulo A y
    /// Módulo B suelen desplegarse por separado; comparten aquí el mismo <see cref="ServiceProvider"/>
    /// solo por simplicidad del ejemplo — lo que garantiza el desacople real entre ambos es que
    /// Facturación solo conoce el contrato público del evento, nunca las tablas internas de Pedidos, ver
    /// <see cref="SampleEventingDbContext"/>): persistencia real (Outbox/Inbox), MediatR/pipeline real,
    /// adapter Kafka real (F3-02) y el relay de Outbox real (F3-03, con <c>PollingInterval</c> reducido
    /// para no alargar el test).
    /// </summary>
    private static async Task<ServiceProvider> BuildProviderAsync(
        string connectionString,
        KafkaMessagingOptions kafkaOptions,
        RecordingEventPublisherDecorator recordingPublisher)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<SampleEventingDbContext>(connectionString);
        services.AddSharedApplication(typeof(ConfirmarPedidoCommand).Assembly);

        services.AddSingleton(kafkaOptions);
        services.AddSingleton<IProducer<string, byte[]>>(
            _ => new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(kafkaOptions)).Build());
        // F3-13: decorador que envuelve el KafkaEventPublisher real (F3-02) — deja pasar cada
        // publicación tal cual hacia Kafka y además la recuerda, para poder simular más adelante una
        // redelivery/duplicado (F3-04) republicando el MISMO evento (mismo EventId) sin tener que
        // reconstruirlo desde el handler de negocio (que nunca lo expone directamente).
        services.AddSingleton<IEventPublisher>(sp =>
        {
            recordingPublisher.SetInner(new KafkaEventPublisher(sp.GetRequiredService<IProducer<string, byte[]>>()));
            return recordingPublisher;
        });
        services.AddSingleton<IEventPublishFailureClassifier, KafkaEventPublishFailureClassifier>();

        // F3-03: relay real (OutboxBatchProcessor + OutboxPublisherBackgroundService) — sondeo rápido
        // para no alargar el test, pero es el MISMO worker que correría en producción, no un loop
        // manual como en los tests aislados de F3-03/F3-07.
        services.AddSharedOutboxPublisher(options => options.PollingInterval = TimeSpan.FromMilliseconds(100));

        // Módulo B (Facturación): en una aplicación real, este registro vive en la composición/módulo
        // propio de Facturación (mismo patrón que ProductosModule en Sample.Api) — acá se registra
        // directo en el host de referencia por tratarse de un único proceso de ejemplo.
        services.AddScoped<IEventConsumer<PedidoConfirmadoIntegrationEvent>, PedidoConfirmadoEventConsumer>();

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SampleEventingDbContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    /// <summary>
    /// Criterio de aceptación literal de F3-13 ("Consistencia eventual comprobada"): un comando de
    /// negocio confirmado en el Módulo A termina, sin ninguna transacción distribuida, produciendo un
    /// efecto de negocio verificable en el Módulo B — con espera acotada, nunca inmediata (es
    /// EVENTUAL). Además, reutiliza la garantía de deduplicación de Inbox (F3-04) en este mismo
    /// escenario: una segunda entrega del mismo evento no duplica el efecto en el Módulo B.
    /// </summary>
    [Fact]
    public async Task ConfirmarPedido_PropagaAFacturacion_ConConsistenciaEventualYSinDuplicarEnRedelivery()
    {
        var kafkaOptions = BuildKafkaOptions();
        var recordingPublisher = new RecordingEventPublisherDecorator();
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString(), kafkaOptions, recordingPublisher);

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        using var consumerLoopCts = new CancellationTokenSource();
        var consumeAttempts = 0;
        using var consumer = new KafkaEventConsumer<PedidoConfirmadoIntegrationEvent>(
            kafkaOptions,
            "Pedidos.PedidoConfirmado",
            provider.GetRequiredService<IServiceScopeFactory>());

        var consumerLoopTask = Task.Run(async () =>
        {
            while (!consumerLoopCts.IsCancellationRequested)
            {
                try
                {
                    if (await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(1), consumerLoopCts.Token))
                    {
                        Interlocked.Increment(ref consumeAttempts);
                    }
                }
                catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
                {
                    // F3-02: el tópico recién se crea con la primera publicación; la metadata puede
                    // tardar en propagarse. Mismo patrón de tolerancia que
                    // KafkaEventPublisherConsumerIntegrationTests/InboxConsumerIntegrationTests.
                    await Task.Delay(200, consumerLoopCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, CancellationToken.None);

        try
        {
            var pedidoId = Guid.NewGuid();
            const string cliente = "Cliente de referencia F3-13";
            const decimal monto = 1500m;

            // Paso 1 (Módulo A): comando de negocio real vía MediatR — el pipeline completo
            // (ValidationBehavior/LoggingBehavior/TransactionBehavior) es el que hace el
            // SaveChangesAsync donde el interceptor de Outbox (F1-23) escribe la fila OutboxMessage
            // atómicamente con el Pedido confirmado.
            await using (var commandScope = provider.CreateAsyncScope())
            {
                var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
                var result = await sender.Send(new ConfirmarPedidoCommand(pedidoId, cliente, monto));
                result.IsSuccess.Should().BeTrue("el comando de confirmación de Pedido debe ejecutarse con éxito");
            }

            // Paso 2 (consistencia eventual, caso feliz): el OutboxPublisherBackgroundService real
            // (F3-03) publica la fila a Kafka y el consumidor del Módulo B (F3-02/F3-04) la procesa —
            // espera acotada con polling, nunca inmediata ni con un Thread.Sleep fijo.
            var facturaCreada = await WaitUntilAsync(
                async () =>
                {
                    await using var verificationScope = provider.CreateAsyncScope();
                    var context = verificationScope.ServiceProvider.GetRequiredService<SampleEventingDbContext>();
                    return await context.FacturasPendientes.IgnoreQueryFilters()
                        .AnyAsync(factura => factura.PedidoId == pedidoId);
                },
                OverallTimeout,
                PollInterval);

            facturaCreada.Should().BeTrue(
                "el Módulo B debe reflejar, con consistencia EVENTUAL (nunca inmediata), el pedido confirmado por el Módulo A");

            await using (var verificationScope = provider.CreateAsyncScope())
            {
                var context = verificationScope.ServiceProvider.GetRequiredService<SampleEventingDbContext>();
                var facturas = await context.FacturasPendientes.IgnoreQueryFilters()
                    .Where(factura => factura.PedidoId == pedidoId)
                    .ToListAsync();

                facturas.Should().ContainSingle();
                facturas[0].Cliente.Should().Be(cliente);
                facturas[0].Monto.Should().Be(monto);
            }

            var attemptsAfterFirstDelivery = await WaitUntilAsync(
                () => Task.FromResult(consumeAttempts >= 1),
                OverallTimeout,
                PollInterval);
            attemptsAfterFirstDelivery.Should().BeTrue("el consumidor debe haber procesado la primera entrega del evento");

            // Paso 3 (duplicados no repiten efectos, F3-04, reutilizado en este escenario completo):
            // republica el MISMO evento (mismo EventId, capturado por el decorador al pasar por el
            // publisher real) — representa una reentrega de Kafka o el duplicado aceptable del relay de
            // Outbox si el proceso muriera entre publicar y marcar la fila (F3-03).
            var publishedEvent = recordingPublisher.Published.Values.Should().ContainSingle(
                e => e.EventType == "Pedidos.PedidoConfirmado",
                "el evento de PedidoConfirmado debe haber pasado por el publisher exactamente una vez en la primera entrega")
                .Subject;

            await recordingPublisher.RepublishAsync(publishedEvent);

            var secondDeliveryConsumed = await WaitUntilAsync(
                () => Task.FromResult(consumeAttempts >= 2),
                OverallTimeout,
                PollInterval);
            secondDeliveryConsumed.Should().BeTrue("el broker debe haber entregado el duplicado y el consumidor debe haberlo procesado (aunque lo descarte por Inbox)");

            await using (var verificationScope = provider.CreateAsyncScope())
            {
                var context = verificationScope.ServiceProvider.GetRequiredService<SampleEventingDbContext>();
                var facturasTrasDuplicado = await context.FacturasPendientes.IgnoreQueryFilters()
                    .Where(factura => factura.PedidoId == pedidoId)
                    .ToListAsync();

                facturasTrasDuplicado.Should().ContainSingle(
                    "el efecto de negocio en el Módulo B no debe duplicarse aunque el evento se entregue dos veces (Inbox, F3-04)");
            }
        }
        finally
        {
            await consumerLoopCts.CancelAsync();
            await consumerLoopTask;

            foreach (var hostedService in hostedServices)
            {
                await hostedService.StopAsync(CancellationToken.None);
            }
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout, TimeSpan pollInterval)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (true)
        {
            if (await predicate())
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(pollInterval);
        }
    }
}

/// <summary>
/// Decorador de <see cref="IEventPublisher"/> dedicado a esta prueba de referencia (F3-13): deja pasar
/// cada publicación hacia el <see cref="KafkaEventPublisher"/> real que envuelve y recuerda el último
/// <see cref="IIntegrationEvent"/> publicado por <see cref="IIntegrationEvent.EventId"/> — permite
/// simular una redelivery/duplicado real republicando exactamente el mismo evento (mismo EventId, mismo
/// payload) que ya pasó por el relay de Outbox real, sin tener que reconstruirlo a mano (el handler de
/// negocio nunca expone directamente el DomainEvent que levantó).
/// </summary>
public sealed class RecordingEventPublisherDecorator : IEventPublisher
{
    private readonly ConcurrentDictionary<Guid, IIntegrationEvent> _published = new();
    private IEventPublisher? _inner;

    public IReadOnlyDictionary<Guid, IIntegrationEvent> Published => _published;

    public void SetInner(IEventPublisher inner) => _inner = inner;

    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        await Inner.PublishAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        _published[integrationEvent.EventId] = integrationEvent;
    }

    public async Task PublishAsync(IEnumerable<IIntegrationEvent> integrationEvents, CancellationToken cancellationToken = default)
    {
        foreach (var integrationEvent in integrationEvents)
        {
            await PublishAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Republica un evento ya visto (mismo EventId) directamente contra el broker real, simulando una redelivery/duplicado.</summary>
    public Task RepublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) =>
        Inner.PublishAsync(integrationEvent, cancellationToken);

    private IEventPublisher Inner => _inner ?? throw new InvalidOperationException(
        $"{nameof(RecordingEventPublisherDecorator)} no tiene un publisher interno configurado — llamar {nameof(SetInner)} antes de publicar.");
}
