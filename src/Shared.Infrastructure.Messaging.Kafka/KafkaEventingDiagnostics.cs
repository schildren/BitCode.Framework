using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Confluent.Kafka;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Instrumentación OpenTelemetry (F3-10) de <see cref="KafkaEventPublisher"/> y
/// <see cref="KafkaEventConsumer{TEvent}"/>: métricas de publish/consume/error/DLQ (<see cref="Meter"/>) y
/// correlación de trazas de punta a punta productor→consumidor (<see cref="ActivitySource"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué un <see cref="Meter"/>/<see cref="ActivitySource"/> propios en vez de reutilizar los que ya
/// registra <c>Shared.Infrastructure.Observability</c>:</b> ese proyecto solo envuelve el WIRING del SDK de
/// OpenTelemetry (<c>AddOpenTelemetry().WithTracing()/.WithMetrics()</c>) — no expone ningún
/// <see cref="Meter"/>/<see cref="ActivitySource"/> propio que otros proyectos puedan instrumentar (no hay
/// convención previa en el repo, ver <c>docs/guia-observabilidad-eventos.md</c>). Este proyecto
/// (<c>Shared.Infrastructure.Messaging.Kafka</c>) no referencia <c>Shared.Infrastructure.Observability</c> a
/// propósito (evita una dependencia de un adapter de broker concreto hacia el wiring de un host, que además
/// nunca es obligatorio: un proyecto consumidor puede usar Kafka sin usar el paquete de observabilidad
/// compartido) — en cambio, expone <see cref="MeterName"/>/<see cref="ActivitySourceName"/> como constantes
/// públicas para que CUALQUIER wiring de OpenTelemetry (el de este framework u otro) pueda suscribirse
/// (<c>.AddMeter(KafkaEventingDiagnostics.MeterName)</c> / <c>.AddSource(KafkaEventingDiagnostics.ActivitySourceName)</c>)
/// sin necesitar una referencia de proyecto — <see cref="Meter"/> y <see cref="ActivitySource"/> de
/// <c>System.Diagnostics.Metrics</c>/<c>System.Diagnostics</c> ya son el mecanismo de desacople estándar de
/// .NET para esto (cualquier listener se registra por NOMBRE, nunca por referencia directa al productor).
/// </para>
/// <para>
/// <b>Correlación end-to-end (criterio de aceptación literal de F3-10):</b> <see cref="InjectTraceContext"/>
/// escribe el <c>Activity.Current</c> vigente en el momento de publicar como headers Kafka
/// <c>traceparent</c>/<c>tracestate</c> (formato W3C Trace Context, el mismo header que ya usan
/// instrumentaciones HTTP estándar de OpenTelemetry — se reutiliza el nombre exacto para que un
/// collector/backend que ya entiende ese formato no necesite ningún mapeo especial para Kafka).
/// <see cref="ExtractTraceContext"/> hace el camino inverso del lado consumidor: si el mensaje trae esos
/// headers, el <see cref="Activity"/> que abre <see cref="KafkaEventConsumer{TEvent}"/> se crea como hijo
/// REMOTO de ese contexto (mismo <c>TraceId</c> que el <see cref="Activity"/> del publisher, un
/// <c>SpanId</c> nuevo) — así una traza puede seguirse de punta a punta a través del broker, algo que
/// ningún mecanismo de correlación in-process (como <c>Activity.Current</c> solo) puede lograr por sí solo
/// una vez que el mensaje cruza a otro proceso/máquina.
/// </para>
/// <para>
/// <b>Qué NO cubre esta clase:</b> no crea ningún exporter ni collector — sin que el proceso host llame
/// <c>.AddMeter(MeterName)</c>/<c>.AddSource(ActivitySourceName)</c> (por ejemplo, vía
/// <c>Shared.Infrastructure.Observability.AddSharedObservability</c>), <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
/// devuelve <see langword="null"/> (sin listener, no hay sampling) y los <see cref="Counter{T}"/>/
/// <see cref="Histogram{T}"/> igual acumulan pero nunca se exportan a ningún backend — ver
/// <c>docs/guia-observabilidad-eventos.md</c> para el wiring completo y los paneles/alertas sugeridos.
/// </para>
/// </remarks>
public static class KafkaEventingDiagnostics
{
    /// <summary>Nombre del <see cref="Meter"/> de este proyecto — usar con <c>MeterProviderBuilder.AddMeter</c>.</summary>
    public const string MeterName = "BitCode.Framework.Messaging.Kafka";

    /// <summary>Nombre del <see cref="ActivitySource"/> de este proyecto — usar con <c>TracerProviderBuilder.AddSource</c>.</summary>
    public const string ActivitySourceName = "BitCode.Framework.Messaging.Kafka";

    /// <summary>Header Kafka W3C Trace Context (mismo nombre que usan las instrumentaciones HTTP estándar de OpenTelemetry).</summary>
    internal const string TraceParentHeader = "traceparent";

    /// <summary>Header Kafka W3C Trace Context (estado adicional opcional del <c>traceparent</c>).</summary>
    internal const string TraceStateHeader = "tracestate";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> PublishedMessages = Meter.CreateCounter<long>(
        "bitcode.messaging.publish.count",
        unit: "{message}",
        description: "Eventos de integración publicados vía KafkaEventPublisher, por EventType/tópico/resultado (success|failure).");

    private static readonly Histogram<double> PublishDuration = Meter.CreateHistogram<double>(
        "bitcode.messaging.publish.duration",
        unit: "ms",
        description: "Duración de KafkaEventPublisher.PublishAsync para un único evento, por EventType/tópico/resultado.");

    private static readonly Counter<long> ConsumedMessages = Meter.CreateCounter<long>(
        "bitcode.messaging.consume.count",
        unit: "{message}",
        description: "Mensajes consumidos vía KafkaEventConsumer<TEvent>, por EventType/resultado (processed|duplicate|poison|exhausted|failure).");

    private static readonly Histogram<double> ConsumeDuration = Meter.CreateHistogram<double>(
        "bitcode.messaging.consume.duration",
        unit: "ms",
        description: "Duración del procesamiento de un mensaje consumido (deserialización + Inbox + handler), por EventType/resultado.");

    private static readonly Counter<long> DeadLetteredMessages = Meter.CreateCounter<long>(
        "bitcode.messaging.dlq.count",
        unit: "{message}",
        description: "Mensajes aislados a un tópico dead-letter (F3-08/F3-09), por EventType/motivo (PoisonMessage|RetriesExhausted).");

    internal static void RecordPublish(string eventType, string topic, string outcome, double elapsedMs)
    {
        var tag1 = new KeyValuePair<string, object?>("bitcode.messaging.event_type", eventType);
        var tag2 = new KeyValuePair<string, object?>("messaging.destination.name", topic);
        var tag3 = new KeyValuePair<string, object?>("bitcode.messaging.outcome", outcome);

        PublishedMessages.Add(1, tag1, tag2, tag3);
        PublishDuration.Record(elapsedMs, tag1, tag2, tag3);
    }

    internal static void RecordConsume(string eventType, string outcome, double elapsedMs)
    {
        var tag1 = new KeyValuePair<string, object?>("bitcode.messaging.event_type", eventType);
        var tag2 = new KeyValuePair<string, object?>("bitcode.messaging.outcome", outcome);

        ConsumedMessages.Add(1, tag1, tag2);
        ConsumeDuration.Record(elapsedMs, tag1, tag2);
    }

    /// <summary>
    /// Registra un <see cref="ObservableGauge{T}"/> de lag de consumo para UN <see cref="KafkaEventConsumer{TEvent}"/>
    /// (cada instancia crea el suyo — múltiples instrumentos con el mismo nombre en el mismo
    /// <see cref="Meter"/>, diferenciados por los tags que devuelve <paramref name="observeValues"/>, es un
    /// patrón soportado por el modelo de métricas de <c>System.Diagnostics.Metrics</c>: el SDK de
    /// OpenTelemetry los agrega por tag, no por instancia de <see cref="ObservableGauge{T}"/>).
    /// </summary>
    internal static ObservableGauge<long> CreateConsumerLagGauge(Func<IEnumerable<Measurement<long>>> observeValues) =>
        Meter.CreateObservableGauge(
            "bitcode.messaging.consumer.lag",
            observeValues,
            unit: "{message}",
            description: "Diferencia entre el high watermark del tópico y la posición actual del consumidor, por partición asignada (F3-10). Sampleado bajo demanda por el exporter de métricas, best-effort.");

    internal static void RecordDeadLetter(string eventType, string reason)
    {
        DeadLetteredMessages.Add(
            1,
            new KeyValuePair<string, object?>("bitcode.messaging.event_type", eventType),
            new KeyValuePair<string, object?>("bitcode.messaging.dlq_reason", reason));
    }

    /// <summary>
    /// Copia el <c>TraceId</c>/<c>SpanId</c> vigente (<see cref="Activity.Current"/>, si hay alguno — el
    /// propio <see cref="Activity"/> de publish que abre <see cref="KafkaEventPublisher"/>, o el ambiente
    /// del llamador si nadie está escuchando <see cref="ActivitySource"/>) como headers W3C Trace Context
    /// del mensaje Kafka — best-effort: si no hay ningún <see cref="Activity"/> vigente, no agrega nada.
    /// </summary>
    internal static void InjectTraceContext(Headers headers)
    {
        var current = Activity.Current;
        if (current is null)
        {
            return;
        }

        var traceParent = current.Id ?? current.Context.ToString();
        headers.Add(TraceParentHeader, Encoding.UTF8.GetBytes(traceParent ?? string.Empty));
        if (!string.IsNullOrEmpty(current.TraceStateString))
        {
            headers.Add(TraceStateHeader, Encoding.UTF8.GetBytes(current.TraceStateString));
        }
    }

    /// <summary>
    /// Recupera el <see cref="ActivityContext"/> W3C Trace Context de los headers de un mensaje Kafka
    /// (contraparte de <see cref="InjectTraceContext"/>), para que <see cref="KafkaEventConsumer{TEvent}"/>
    /// abra su <see cref="Activity"/> de consumo como hijo remoto — <see langword="default"/> si el mensaje
    /// no trae <c>traceparent</c> (mensaje publicado sin ningún <see cref="Activity"/> vigente, o por un
    /// productor que no es <see cref="KafkaEventPublisher"/>).
    /// </summary>
    internal static ActivityContext ExtractTraceContext(Headers? headers)
    {
        if (headers is null || !headers.TryGetLastBytes(TraceParentHeader, out var traceParentBytes))
        {
            return default;
        }

        var traceParent = Encoding.UTF8.GetString(traceParentBytes);
        var traceState = headers.TryGetLastBytes(TraceStateHeader, out var traceStateBytes)
            ? Encoding.UTF8.GetString(traceStateBytes)
            : null;

        return ActivityContext.TryParse(traceParent, traceState, out var context) ? context : default;
    }
}
