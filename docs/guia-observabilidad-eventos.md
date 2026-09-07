# Guía de observabilidad de eventos (F3-10)

Instrumentación OpenTelemetry de la plataforma de eventos (Fase 3): `KafkaEventPublisher`/
`KafkaEventConsumer<TEvent>` (F3-02/F3-04/F3-09, `Shared.Infrastructure.Messaging.Kafka`) y
`OutboxBatchProcessor` (F3-03/F3-07/F3-08, `Shared.Infrastructure.Persistence`).

## Qué hace este repo por vos (instrumentación) y qué no (dashboard/alertas)

BitCode.Framework es una librería, no una aplicación desplegada — no trae Grafana ni Prometheus. Desde
F4-10 sí incluye, como **infraestructura de referencia** (`k8s/otel-collector/`, no parte de ningún
paquete NuGet), un OpenTelemetry Collector centralizado — ver `docs/guia-otel-collector.md`. Lo que SÍ
entrega esta tarea (F3-10):

- **Métricas** (`System.Diagnostics.Metrics.Meter`/`Counter`/`Histogram`/`ObservableGauge`) de publish,
  consume, error y DLQ, verificadas con tests (`MeterListener`).
- **Trazas distribuidas correlacionadas de punta a punta** (`System.Diagnostics.ActivitySource`/`Activity`,
  W3C Trace Context propagado en headers Kafka), verificadas con un test de integración contra un broker
  real (`ActivityListener`).
- Un `overload` de `AddSharedObservability` (`Shared.Infrastructure.Observability`) para que un host
  wiree estas métricas/trazas al SDK de OpenTelemetry sin que ese proyecto referencie Kafka/Persistence.

Lo que NO entrega (fuera del alcance de una librería, vive en el proyecto consumidor/infraestructura real):
el dashboard de Grafana en sí y las reglas de alerta de Prometheus/Alertmanager — más abajo se documenta
qué panel/alerta construir con cada métrica, para que un operador solo tenga que traducirlo a su backend
concreto.

## Wiring (proyecto consumidor)

```csharp
services.AddSharedObservability(
    configuration,
    additionalMeterNames: [KafkaEventingDiagnostics.MeterName, OutboxDiagnostics.MeterName],
    additionalActivitySourceNames: [KafkaEventingDiagnostics.ActivitySourceName]);

services.AddSharedMessagingKafka(configuration);
services.AddSharedOutboxPublisher();
```

Sin pasar `additionalMeterNames`/`additionalActivitySourceNames`, las métricas/trazas se siguen generando
(los `Counter`/`Histogram`/`Activity` no dependen de que alguien escuche) pero nunca se exportan a ningún
backend — `MeterProviderBuilder.AddMeter`/`TracerProviderBuilder.AddSource` es lo que activa la exportación
real vía OTLP (u otro exporter que el host configure).

## Convención de nombres

No existía ninguna convención previa de `Meter`/`ActivitySource` manual en el repo (`AddAspNetCoreInstrumentation`/
`AddHttpClientInstrumentation`/`AddRuntimeInstrumentation` son instrumentación automática de paquetes de
OpenTelemetry, no algo que este framework instrumentó a mano) — se definió una nueva:

- **Nombres de `Meter`/`ActivitySource`**: `BitCode.Framework.<Proyecto>` (ej.
  `BitCode.Framework.Messaging.Kafka`), expuestos como constantes públicas
  (`KafkaEventingDiagnostics.MeterName`/`.ActivitySourceName`, `OutboxDiagnostics.MeterName`) para que
  cualquier wiring externo pueda suscribirse por nombre sin depender de una referencia de proyecto — el
  mecanismo de desacople estándar de `System.Diagnostics.Metrics`/`System.Diagnostics`.
- **Nombres de métricas**: `bitcode.<dominio>.<métrica>`, con tags de contexto reutilizando el vocabulario
  de las [OpenTelemetry Semantic Conventions de mensajería](https://opentelemetry.io/docs/specs/semconv/messaging/)
  donde existe uno estable (`messaging.system`, `messaging.destination.name`, `messaging.operation`) y un
  prefijo propio `bitcode.*` para lo que es específico de este framework (`bitcode.messaging.event_type`,
  `bitcode.messaging.outcome`, `bitcode.messaging.dlq_reason`) — las convenciones de métricas de mensajería
  de OTel siguen "experimental"/en evolución al momento de esta tarea, así que se prefirió un namespace
  propio estable antes que atarse a nombres que puedan cambiar en una versión futura del estándar.
- **Header de correlación**: `traceparent`/`tracestate` (formato W3C Trace Context, RFC del W3C), el mismo
  nombre que usan las instrumentaciones HTTP estándar de OpenTelemetry — un collector/backend que ya
  entiende ese formato no necesita ningún mapeo especial para Kafka.

## Métricas expuestas

### `Shared.Infrastructure.Messaging.Kafka` (`KafkaEventingDiagnostics.MeterName`)

| Métrica | Tipo | Unidad | Tags | Descripción |
|---|---|---|---|---|
| `bitcode.messaging.publish.count` | Counter\<long\> | `{message}` | `bitcode.messaging.event_type`, `messaging.destination.name`, `bitcode.messaging.outcome` (`success`\|`failure`) | Eventos publicados por `KafkaEventPublisher`. |
| `bitcode.messaging.publish.duration` | Histogram\<double\> | `ms` | ídem | Duración de `PublishAsync` por evento. |
| `bitcode.messaging.consume.count` | Counter\<long\> | `{message}` | `bitcode.messaging.event_type`, `bitcode.messaging.outcome` (`processed`\|`duplicate`\|`poison`\|`exhausted`\|`failure`) | Mensajes consumidos por `KafkaEventConsumer<TEvent>`. |
| `bitcode.messaging.consume.duration` | Histogram\<double\> | `ms` | ídem | Duración del procesamiento (deserialización + Inbox + handler) por mensaje. |
| `bitcode.messaging.dlq.count` | Counter\<long\> | `{message}` | `bitcode.messaging.event_type`, `bitcode.messaging.dlq_reason` (`PoisonMessage`\|`RetriesExhausted`) | Mensajes aislados a un tópico dead-letter (F3-08/F3-09). |
| `bitcode.messaging.consumer.lag` | ObservableGauge\<long\> | `{message}` | `messaging.destination.name`, `bitcode.messaging.partition`, `bitcode.messaging.event_type` | Diferencia entre el high watermark del tópico y la posición actual del consumidor, por partición asignada. **Best-effort**: sampleado bajo demanda (pull) por el exporter, requiere ida y vuelta al broker (`QueryWatermarkOffsets`, timeout 1s); se omite (sin lanzar) si el broker no responde a tiempo o hay un rebalance en curso. |

`outcome=processed` vs. `outcome=duplicate` distingue si `IInboxMessageProcessor.ProcessAsync` (F1-24)
ejecutó el handler de negocio o lo descartó por ser un duplicado real — la deduplicación en sí (criterio de
aceptación de F3-04) no cambia; esto solo la hace observable.

### `Shared.Infrastructure.Persistence` (`OutboxDiagnostics.MeterName`)

| Métrica | Tipo | Unidad | Tags | Descripción |
|---|---|---|---|---|
| `bitcode.outbox.batch.duration` | Histogram\<double\> | `ms` | `bitcode.outbox.claimed` (cantidad de filas reclamadas en el ciclo) | Duración de un ciclo completo de `OutboxBatchProcessor.ProcessBatchAsync`. |
| `bitcode.outbox.batch.rows` | Counter\<long\> | `{row}` | `bitcode.outbox.outcome` (`published`\|`skipped_internal`\|`failed_retryable`\|`exhausted`) | Filas de `OutboxMessage` procesadas por ciclo, por resultado. `failed_retryable` y `exhausted` son disjuntos (`OutboxBatchResult.Exhausted` es subconjunto de `.Failed`, ver remarks de esa clase). |
| `bitcode.outbox.dlq.count` | Counter\<long\> | `{row}` | `bitcode.outbox.dlq.outcome` (`success`\|`failure`) | Intentos de publicación a un tópico dead-letter tras agotar reintentos (F3-08), del lado del relay de Outbox. |

La correlación de trazas de un evento publicado por el relay de Outbox no necesita un span propio: cada
fila publicada pasa por `IEventPublisher.PublishAsync`, cuya implementación concreta (`KafkaEventPublisher`)
ya abre su propio span — `OutboxBatchProcessor` no lo duplica.

## Correlación end-to-end (criterio de aceptación literal)

`KafkaEventPublisher.PublishOneAsync` abre un `Activity` (`ActivityKind.Producer`) usando
`KafkaEventingDiagnostics.ActivitySource` y copia el `TraceId`/`SpanId` vigente (`Activity.Current`, ya sea
ese mismo span o el ambiente del llamador) a los headers Kafka `traceparent`/`tracestate` (W3C Trace
Context) — la misma copia de conveniencia que ya hacían `bitcode-event-type`/`bitcode-schema-version`/etc.
(F3-02).

`KafkaEventConsumer<TEvent>.ConsumeAndHandleOnceAsync` lee esos headers (`KafkaEventingDiagnostics.ExtractTraceContext`)
y abre su propio `Activity` (`ActivityKind.Consumer`) como **hijo remoto** de ese contexto — mismo `TraceId`
que el publisher, un `SpanId` nuevo, `ParentSpanId` apuntando al span del publisher. Esto vale tanto para
un mensaje "feliz" como para uno poison/agotado (el `Activity` del consumidor se abre ANTES de intentar
deserializar, así que cubre también esos casos).

Sin que el host registre un listener (`TracerProviderBuilder.AddSource(KafkaEventingDiagnostics.ActivitySourceName)`,
vía `AddSharedObservability` o directamente), `ActivitySource.StartActivity` devuelve `null` (sin costo:
no hay sampling) — la propagación de headers igual ocurre (usa `Activity.Current` del llamador si existe),
pero no hay ningún span propio de BitCode que ver en un backend de trazas.

## Dashboard sugerido (guía operativa, a construir en el backend de cada consumidor)

| Panel | Query conceptual | Qué responde |
|---|---|---|
| Tasa de publicación | `rate(bitcode_messaging_publish_count[5m])` por `bitcode_messaging_event_type` | Volumen de eventos publicados por tipo. |
| Tasa de error de publicación | `rate(bitcode_messaging_publish_count{outcome="failure"}[5m]) / rate(bitcode_messaging_publish_count[5m])` | % de publicaciones fallidas — un salto sostenido indica broker degradado o mal configurado. |
| Latencia de publish (p95/p99) | `histogram_quantile(0.95, bitcode_messaging_publish_duration)` | Percentiles de duración de `PublishAsync`. |
| Tasa de consumo por resultado | `rate(bitcode_messaging_consume_count[5m])` por `outcome` | Volumen processed/duplicate/poison/exhausted/failure — un aumento de `poison`/`exhausted` es una señal de mensajes/handlers rotos. |
| Lag de consumo | `bitcode_messaging_consumer_lag` por `messaging_destination_name`/`bitcode_messaging_partition` | Mensajes pendientes de consumir — crecimiento sostenido indica un consumidor lento o caído. |
| Tasa de DLQ | `rate(bitcode_messaging_dlq_count[5m])` por `dlq_reason` | Mensajes aislados a dead-letter — cualquier valor > 0 sostenido amerita revisar `docs/runbook-dlq.md`. |
| Ciclo de Outbox | `histogram_quantile(0.95, bitcode_outbox_batch_duration)` y `rate(bitcode_outbox_batch_rows{outcome="exhausted"}[5m])` | Salud del relay de Outbox — filas agotadas del lado emisor. |

## Alertas sugeridas (umbrales de partida, ajustar por SLO real de cada consumidor)

| Alerta | Condición sugerida | Severidad sugerida |
|---|---|---|
| Tasa de error de publicación alta | `outcome="failure" / total > 5%` sostenido 5 minutos | Warning; Critical si > 25% |
| Lag de consumo creciente | `bitcode_messaging_consumer_lag` crece de forma sostenida durante 10 minutos sin bajar | Warning |
| Lag de consumo crítico | `bitcode_messaging_consumer_lag > <umbral absoluto acordado por el consumidor>` | Critical |
| DLQ con actividad | `rate(bitcode_messaging_dlq_count[15m]) > 0` | Warning (cualquier mensaje en DLQ amerita revisión manual, ver runbook) |
| Outbox con filas agotadas | `rate(bitcode_outbox_batch_rows{outcome="exhausted"}[15m]) > 0` | Warning |
| Ciclo de Outbox degradado | p95 de `bitcode_outbox_batch_duration` supera el `PollingInterval` configurado | Warning (el relay puede empezar a acumular atraso) |

## Qué NO cubre esta tarea (fuera de alcance, documentado explícitamente)

- No se agregó ningún exporter/collector real al repo (ni siquiera de desarrollo) — el wiring de
  `AddSharedObservability` ya soportaba OTLP desde antes de F3-10 (`OpenTelemetryOptions.OtlpEndpoint`).
- No se define ningún archivo de dashboard de Grafana (`.json`) ni de reglas de Prometheus/Alertmanager —
  las tablas de arriba son la guía conceptual; la traducción a un backend concreto depende de qué exporter
  use cada consumidor (Prometheus, OTLP hacia otro backend, etc.), fuera del control de esta librería.
- La propagación de `tenant_id`/`user_id`/`region`/`instance` como atributos de resource/baggage (Fase 4,
  "Telemetría mínima") no es parte de esta tarea — los tags agregados acá son específicos del dominio de
  mensajería (`event_type`, `outcome`, `dlq_reason`), no el conjunto completo de campos de telemetría
  mínima que exige la Fase 4 para request/comando/job.
