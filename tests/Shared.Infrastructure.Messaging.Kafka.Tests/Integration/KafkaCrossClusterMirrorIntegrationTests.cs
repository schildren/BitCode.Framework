using System.Text;
using BitCode.Framework.Shared.Application.Inbox;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FluentAssertions;
using Testcontainers.Kafka;
using Xunit.Abstractions;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests.Integration;

/// <summary>
/// F5-05 (Fase 5 — Disaster Recovery y multi-región): criterio de aceptación literal "Mensajes
/// preservados" — contra DOS clústeres Kafka reales e independientes (dos contenedores
/// <c>Testcontainers.Kafka</c>, sin broker ni almacenamiento compartido, exactamente como serían un
/// clúster "primario" y un clúster "DR" en regiones distintas), un proceso de mirroring real (un
/// consumer que lee del tópico primario y republica en el tópico réplica del otro clúster,
/// preservando <c>Key</c> y el header <c>message-id</c>) demuestra que:
///
/// <list type="number">
/// <item>ningún mensaje YA CONFIRMADO en el destino se pierde cuando el clúster origen se interrumpe
/// (<see cref="Mirror_PreservesAlreadyConfirmedMessages_AfterSourceClusterInterruption"/>);</item>
/// <item>la semántica real es "al menos una vez" (nunca "exactamente una vez" de punta a punta — ver
/// sección 3.2 del Plan Maestro): un reinicio del proceso de mirroring desde el último offset
/// confirmado en el origen puede reproducir mensajes ya entregados al destino, y es la deduplicación
/// idempotente del lado consumidor (<see cref="IInboxMessageProcessor"/>, F1-24/F3-04) la que evita que
/// esa redelivery duplique el efecto de negocio
/// (<see cref="Mirror_ResumedAfterCrashBeforeCommit_RedeliversDuplicates_ButDownstreamInboxProcessesOnce"/>).
/// </item>
/// </list>
/// </summary>
/// <remarks>
/// Ver <c>docs/replicacion-kafka-fase5.md</c> sección 4 para el razonamiento completo de por qué esta
/// prueba usa un mirror simple (consumer→producer) en vez de MirrorMaker 2 / Cluster Linking real (no
/// viable de levantar en Testcontainers en este entorno) — el documento de arquitectura recomienda
/// MirrorMaker 2 (o Cluster Linking, si es Confluent) como mecanismo productivo; esta prueba demuestra
/// el mismo fenómeno de fondo (replicación asíncrona basada en consumo+republicación, semántica al
/// menos una vez) con un mecanismo real y ejecutable en un solo host de desarrollo.
///
/// No usa <see cref="BitCode.Framework.Shared.Testing.KafkaContainerFixture"/> ni la
/// <c>KafkaCollection</c> compartida: esta prueba necesita, a propósito, DOS clústeres Kafka
/// completamente independientes (dos contenedores, cada uno su propio broker), igual que
/// <c>SqlLogShippingRpoIntegrationTests</c> (F5-04) necesita dos instancias SQL Server independientes
/// para que la "interrupción del origen" sea real y no una operación dentro del mismo broker.
/// </remarks>
public sealed class KafkaCrossClusterMirrorIntegrationTests : IAsyncLifetime
{
    private const string PrimaryTopic = "orders-primary";
    private const string ReplicaTopic = "orders-replica";
    private const string MessageIdHeaderKey = "message-id";

    private readonly KafkaContainer _primaryCluster = new KafkaBuilder().Build();
    private readonly KafkaContainer _replicaCluster = new KafkaBuilder().Build();
    private readonly ITestOutputHelper _output;

    public KafkaCrossClusterMirrorIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync() =>
        Task.WhenAll(_primaryCluster.StartAsync(), _replicaCluster.StartAsync());

    public Task DisposeAsync() =>
        Task.WhenAll(_primaryCluster.DisposeAsync().AsTask(), _replicaCluster.DisposeAsync().AsTask());

    /// <summary>
    /// Criterio de aceptación de F5-05 ("Mensajes preservados"): se publican 30 mensajes ordenados
    /// (misma <c>Key</c> de partición — el equivalente de particionar por <c>AggregateId</c>/
    /// <c>TenantId</c> de la Fase 3, un único flujo ordenado) en el tópico del clúster primario. Un
    /// proceso de mirroring real los consume y republica, EN ORDEN, en el tópico del clúster réplica,
    /// confirmando (commit) el offset en el primario solo DESPUÉS de que la republicación en el
    /// destino fue reconocida (<c>acks=all</c> + <c>Flush</c>) — el orden correcto para que "confirmado
    /// en destino" implique "seguro", nunca al revés. A mitad de camino (después de mirrar solo los
    /// primeros 18 de 30), el clúster primario se detiene (<c>StopAsync</c> — una interrupción real del
    /// proceso de SO del broker, no un mock) simulando la caída de la región primaria antes de que el
    /// mirroring termine.
    ///
    /// La prueba verifica que los 18 mensajes ya confirmados en el destino ANTES de la interrupción
    /// siguen íntegros y en orden en el tópico réplica después de que el primario dejó de existir —
    /// nunca se exige (ni se afirma) que los 12 mensajes restantes, todavía no mirrados en el momento
    /// de la caída, aparezcan: esos forman parte del RPO del mecanismo (la ventana de mensajes
    /// confirmados en el origen que el mirror aún no había alcanzado a replicar), exactamente el mismo
    /// fenómeno medido para SQL en F5-04 (<c>docs/replicacion-sql-fase5.md</c>), aplicado aquí a Kafka.
    /// </summary>
    [Fact]
    public async Task Mirror_PreservesAlreadyConfirmedMessages_AfterSourceClusterInterruption()
    {
        const int totalMessages = 30;
        const int messagesToMirrorBeforeInterruption = 18;
        const string partitionKey = "pedido-42";

        await CreateSinglePartitionTopicAsync(_primaryCluster, PrimaryTopic);
        await CreateSinglePartitionTopicAsync(_replicaCluster, ReplicaTopic);

        // 1) Carga real en el clúster primario: 30 mensajes, misma Key de partición (orden garantizado
        //    dentro de esa partición — el mismo principio de F3-05, particionamiento por
        //    AggregateId/TenantId), cada uno con un message-id estable en un header (lo que en
        //    producción sería, por ejemplo, el EventId del evento de integración).
        var messageIds = Enumerable.Range(0, totalMessages).Select(_ => Guid.NewGuid().ToString()).ToList();
        await ProduceSequentialMessagesAsync(_primaryCluster, PrimaryTopic, partitionKey, messageIds);

        // 2) Mirror real: consume del primario (grupo de consumidor dedicado, sin auto-commit) y
        //    republica, EN ORDEN, en el clúster réplica — commit del offset en el primario solo tras
        //    confirmar la republicación en el destino.
        using var mirrorConsumer = BuildManualCommitConsumer(_primaryCluster, PrimaryTopic, "mirror-group");
        using var mirrorProducer = BuildProducer(_replicaCluster);

        for (var i = 0; i < messagesToMirrorBeforeInterruption; i++)
        {
            var consumeResult = mirrorConsumer.Consume(TimeSpan.FromSeconds(10));
            consumeResult.Should().NotBeNull($"el mensaje {i} debía estar disponible en el tópico primario");

            await MirrorOneMessageAsync(mirrorProducer, ReplicaTopic, consumeResult!);
            mirrorConsumer.Commit(consumeResult);
        }

        // 3) Interrupción REAL del clúster primario (proceso de contenedor detenido, no un mock) —
        //    exactamente el momento en que, en un desastre real, el mirroring deja de poder avanzar.
        await _primaryCluster.StopAsync();

        // 4) Verificación: los 18 mensajes ya confirmados en el destino ANTES de la interrupción siguen
        //    íntegros y en el mismo orden en el tópico réplica del OTRO clúster, que sigue vivo.
        var replicatedMessageIds = await ConsumeAllMessageIdsAsync(
            _replicaCluster, ReplicaTopic, expectedCount: messagesToMirrorBeforeInterruption, TimeSpan.FromSeconds(20));

        _output.WriteLine(
            $"Mensajes publicados en el primario: {totalMessages}\n" +
            $"Mensajes mirrados al destino antes de la interrupción: {messagesToMirrorBeforeInterruption}\n" +
            $"Mensajes presentes en el destino tras detener el clúster primario: {replicatedMessageIds.Count}");

        replicatedMessageIds.Should().HaveCount(
            messagesToMirrorBeforeInterruption,
            "ningún mensaje ya confirmado en el destino antes de la interrupción del origen debe perderse");
        replicatedMessageIds.Should().Equal(
            messageIds.Take(messagesToMirrorBeforeInterruption),
            "el mirror preserva el orden de publicación dentro de la misma partición (misma Key), " +
            "igual que el particionamiento por AggregateId/TenantId de la Fase 3 (F3-05)");
    }

    /// <summary>
    /// Complemento necesario del criterio "Mensajes preservados": la Fase 3 fija la semántica real de
    /// la plataforma de eventos como "al menos una vez" (<c>docs/politica-reintentos-eventos.md</c>,
    /// Inbox de F1-24/F3-04), nunca "exactamente una vez" de punta a punta (sección 3.2 del Plan
    /// Maestro lo prohíbe explícitamente). Esta prueba demuestra por qué esa semántica también aplica
    /// al mirroring entre clústeres: si el proceso de mirroring se reinicia después de haber producido
    /// con éxito un mensaje en el destino pero ANTES de confirmar (commit) ese offset en el origen, el
    /// mirror reanudado (misma <c>ConsumerGroupId</c>, retomando desde el último offset SÍ confirmado)
    /// vuelve a consumir y republicar ese mismo mensaje — una duplicación real y observable en el
    /// tópico destino, no simulada.
    ///
    /// La prueba verifica las dos mitades de la garantía real: (a) NINGÚN mensaje se pierde (los 5
    /// mensajes originales llegan al destino, incluso duplicados) y (b) un consumidor final que aplica
    /// deduplicación idempotente por <c>message-id</c> (el mismo <see cref="IInboxMessageProcessor"/>
    /// real de F1-24, aquí con el test double en memoria ya usado por el resto de este proyecto)
    /// procesa el efecto de negocio EXACTAMENTE una vez por mensaje único, pese a la redelivery.
    /// </summary>
    [Fact]
    public async Task Mirror_ResumedAfterCrashBeforeCommit_RedeliversDuplicates_ButDownstreamInboxProcessesOnce()
    {
        const int totalMessages = 5;
        const string partitionKey = "pedido-99";
        const string consumerGroupId = "mirror-group-resume";

        await CreateSinglePartitionTopicAsync(_primaryCluster, PrimaryTopic);
        await CreateSinglePartitionTopicAsync(_replicaCluster, ReplicaTopic);

        var messageIds = Enumerable.Range(0, totalMessages).Select(_ => Guid.NewGuid().ToString()).ToList();
        await ProduceSequentialMessagesAsync(_primaryCluster, PrimaryTopic, partitionKey, messageIds);

        // 1) Primera "corrida" del mirror: consume y republica los 5 mensajes, pero SOLO confirma
        //    (commit) el offset de los primeros 2 — deliberadamente no se hace commit del offset de los
        //    mensajes 3, 4 y 5 aunque ya fueron republicados con éxito en el destino, reproduciendo el
        //    escenario real "el proceso murió después de producir en destino, antes de hacer commit en
        //    el origen".
        using (var firstRunConsumer = BuildManualCommitConsumer(_primaryCluster, PrimaryTopic, consumerGroupId))
        using (var mirrorProducer = BuildProducer(_replicaCluster))
        {
            for (var i = 0; i < totalMessages; i++)
            {
                var consumeResult = firstRunConsumer.Consume(TimeSpan.FromSeconds(10));
                consumeResult.Should().NotBeNull();

                await MirrorOneMessageAsync(mirrorProducer, ReplicaTopic, consumeResult!);

                if (i < 2)
                {
                    firstRunConsumer.Commit(consumeResult);
                }
            }

            // Close() (no solo Dispose) envía un "leave group" explícito al coordinador del grupo de
            // consumidores — sin esto, el coordinador tarda hasta session.timeout.ms en detectar que
            // este miembro se fue, y el resumedConsumer de más abajo (mismo ConsumerGroupId) no
            // recibiría la partición asignada dentro del timeout de esta prueba. En un crash real del
            // proceso de mirroring no habría Close() (por eso el resto del escenario sigue siendo
            // representativo de un reinicio tras una caída, no de un apagado ordenado) — este Close()
            // aquí es solo para que la prueba sea determinística y rápida, no cambia la semántica que
            // se está demostrando (redelivery desde el último offset confirmado).
            firstRunConsumer.Close();
        }

        // 2) Segunda "corrida" (reinicio del proceso de mirroring tras el crash simulado): mismo
        //    ConsumerGroupId, retoma desde el último offset confirmado (después del mensaje 2) — vuelve
        //    a consumir y republicar los mensajes 3, 4 y 5, que ya estaban en el destino.
        using (var resumedConsumer = BuildManualCommitConsumer(_primaryCluster, PrimaryTopic, consumerGroupId))
        using (var mirrorProducer = BuildProducer(_replicaCluster))
        {
            for (var i = 2; i < totalMessages; i++)
            {
                var consumeResult = resumedConsumer.Consume(TimeSpan.FromSeconds(10));
                consumeResult.Should().NotBeNull();

                await MirrorOneMessageAsync(mirrorProducer, ReplicaTopic, consumeResult!);
                resumedConsumer.Commit(consumeResult);
            }
        }

        // 3) El tópico destino real contiene, de forma verificable, la redelivery: 8 mensajes físicos
        //    (5 únicos + 3 duplicados de los mensajes 3, 4, 5) — "al menos una vez" hecho observable,
        //    NUNCA "exactamente una vez" (lo que confirmaría, incorrectamente, que no hay duplicados).
        var allDeliveredMessageIds = await ConsumeAllMessageIdsAsync(
            _replicaCluster, ReplicaTopic, expectedCount: totalMessages + 3, TimeSpan.FromSeconds(20));

        allDeliveredMessageIds.Should().HaveCount(
            totalMessages + 3,
            "el mirror reanudado vuelve a entregar los mensajes 3, 4 y 5 (ya producidos en destino antes " +
            "del commit fallido en origen) — redelivery real, no exactamente-una-vez");
        messageIds.Should().BeSubsetOf(
            allDeliveredMessageIds,
            "ningún mensaje se pierde: los 5 mensajes únicos están presentes en el destino, con o sin duplicados");

        // 4) Deduplicación real, del lado consumidor, con el Inbox idempotente de F1-24/F3-04: procesar
        //    cada entrega física (incluidas las duplicadas) a través de IInboxMessageProcessor debe
        //    ejecutar el handler de negocio EXACTAMENTE una vez por message-id único.
        IInboxMessageProcessor inbox = new InMemoryInboxMessageProcessor();
        var handlerExecutions = new Dictionary<string, int>();

        foreach (var messageId in allDeliveredMessageIds)
        {
            await inbox.ProcessAsync(
                messageId,
                messageType: "orden-creada",
                payload: messageId,
                handler: _ =>
                {
                    handlerExecutions[messageId] = handlerExecutions.GetValueOrDefault(messageId) + 1;
                    return Task.CompletedTask;
                });
        }

        handlerExecutions.Should().HaveCount(totalMessages, "un único message-id por mensaje de negocio real");
        handlerExecutions.Values.Should().OnlyContain(
            executions => executions == 1,
            "el Inbox idempotente descarta la redelivery del mirror: el handler de negocio se ejecuta " +
            "exactamente una vez por mensaje único, pese a que el mirror entregó algunos dos veces");
    }

    private static async Task CreateSinglePartitionTopicAsync(KafkaContainer cluster, string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = cluster.GetBootstrapAddress(),
        }).Build();

        await admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 },
        ]);
    }

    private static async Task ProduceSequentialMessagesAsync(
        KafkaContainer cluster, string topic, string partitionKey, IReadOnlyList<string> messageIds)
    {
        using var producer = BuildProducer(cluster);

        foreach (var messageId in messageIds)
        {
            var message = new Message<string, string>
            {
                Key = partitionKey,
                Value = messageId,
                Headers = [new Header(MessageIdHeaderKey, Encoding.UTF8.GetBytes(messageId))],
            };

            await producer.ProduceAsync(topic, message);
        }

        producer.Flush(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Republica en <paramref name="destinationTopic"/> del clúster destino de <paramref name="producer"/>
    /// el mismo <c>Key</c>, <c>Value</c> y header <c>message-id</c> del mensaje consumido del origen —
    /// <c>acks=all</c> (fijado en <see cref="BuildProducer"/>) + <see cref="IProducer{TKey,TValue}.Flush"/>
    /// garantizan que la republicación fue reconocida por el clúster destino ANTES de que el llamador
    /// haga commit del offset en el origen (el orden correcto: "confirmado en destino" antes que
    /// "avanzar en origen").
    /// </summary>
    private static async Task MirrorOneMessageAsync(
        IProducer<string, string> producer, string destinationTopic, ConsumeResult<string, string> sourceMessage)
    {
        await producer.ProduceAsync(destinationTopic, new Message<string, string>
        {
            Key = sourceMessage.Message.Key,
            Value = sourceMessage.Message.Value,
            Headers = sourceMessage.Message.Headers,
        });

        producer.Flush(TimeSpan.FromSeconds(10));
    }

    private static async Task<List<string>> ConsumeAllMessageIdsAsync(
        KafkaContainer cluster, string topic, int expectedCount, TimeSpan overallTimeout)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = cluster.GetBootstrapAddress(),
            GroupId = $"verificacion-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);

        var results = new List<string>();
        var deadline = DateTime.UtcNow.Add(overallTimeout);
        while (results.Count < expectedCount && DateTime.UtcNow < deadline)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(2));
                if (result is not null && !result.IsPartitionEOF && result.Message is not null)
                {
                    results.Add(ExtractMessageId(result));
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        consumer.Close();
        return results;
    }

    private static string ExtractMessageId(ConsumeResult<string, string> result)
    {
        var header = result.Message.Headers?.FirstOrDefault(h => h.Key == MessageIdHeaderKey);
        return header is not null ? Encoding.UTF8.GetString(header.GetValueBytes()) : result.Message.Value;
    }

    private static IProducer<string, string> BuildProducer(KafkaContainer cluster) =>
        new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = cluster.GetBootstrapAddress(),
            Acks = Acks.All,
        }).Build();

    private static IConsumer<string, string> BuildManualCommitConsumer(KafkaContainer cluster, string topic, string groupId)
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = cluster.GetBootstrapAddress(),
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);
        return consumer;
    }
}
