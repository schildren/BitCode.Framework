# Política de reintentos de eventos de integración (F3-07)

**Tarea:** F3-07 (Fase 3 — Plataforma de eventos) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Trabajo:** Retries — clasificar errores transitorios y permanentes.
**Criterio de aceptación:** "Backoff con jitter y límites".

## Objetivo

Antes de esta tarea, un fallo de publicación en el relay de Outbox (F3-03,
`OutboxBatchProcessor`) se reintentaba en el próximo ciclo de sondeo **sin backoff, sin
clasificación y sin límite máximo** — un error permanente (un tipo de evento irresolvible, un
payload no serializable, un mensaje que el broker rechaza siempre) se reintentaba
indefinidamente, exactamente igual que un error transitorio (el broker caído momentáneamente).
Del lado consumidor (F3-04, `KafkaEventConsumer<TEvent>`), un fallo del handler simplemente
propagaba la excepción tal cual, sin ninguna clasificación ni pausa entre reintentos.

F3-07 introduce, para ambos lados:

1. **Clasificación** de cada excepción como transitoria (reintentar tiene sentido) o permanente
   (reintentar nunca va a funcionar).
2. **Backoff exponencial con jitter** entre reintentos.
3. **Límite máximo de intentos**, tras el cual el mensaje queda marcado como "agotado" — nunca se
   descarta ni se pierde, queda como punto de extensión explícito para F3-08 (DLQ).

Todo esto se mantiene dentro de la semántica **at-least-once** ya establecida por la Fase 3 (Plan
Maestro, "Semántica exigida": "Reprocesamiento: soportado y auditado"): los reintentos nunca
intentan resolver exactly-once de punta a punta (regla dura 3.2 del Plan Maestro, prohibida
explícitamente) — solo acotan cuánto tiempo/cuántas veces se reintenta antes de requerir
intervención.

## Dónde vive cada pieza

| Componente | Proyecto | Rol |
|---|---|---|
| `EventPublishFailureKind` | `Shared.Application.Eventing` | Enum `Transient`/`Permanent`. |
| `IEventPublishFailureClassifier` | `Shared.Application.Eventing` | Contrato: clasifica una `Exception`. |
| `DefaultEventPublishFailureClassifier` | `Shared.Application.Eventing` | Clasificador de reserva: todo transitorio (el valor por defecto más seguro sin información de broker). |
| `EventRetryPolicyOptions` | `Shared.Application.Eventing` | `MaxAttempts` (10), `BaseDelay` (2 s), `MaxDelay` (5 min). |
| `EventRetryBackoff` | `Shared.Application.Eventing` | Cálculo puro: `CalculateDelay`, `IsExhausted`. |
| `EventProcessingExhaustedException` | `Shared.Application.Eventing` | Señaliza "se agotó el margen de reintentos" del lado consumidor (punto de extensión para F3-08). |
| `KafkaEventPublishFailureClassifier` | `Shared.Infrastructure.Messaging.Kafka` | Clasificación real de `Confluent.Kafka.ProduceException` (`Error.IsFatal`/`Error.Code`). |
| `OutboxBatchProcessor` (`RegisterFailure`) | `Shared.Infrastructure.Persistence` | Aplica la política del lado publicador contra `OutboxMessage`. |
| `KafkaEventConsumer<TEvent>` | `Shared.Infrastructure.Messaging.Kafka` | Aplica la política del lado consumidor (backoff bloqueante, conteo en memoria). |

`IEventPublishFailureClassifier` vive en `Shared.Application.Eventing` (no en
`Shared.Infrastructure.Messaging.Kafka`) por el mismo motivo que `IEventPublisher` (F3-01):
`OutboxBatchProcessor` (`Shared.Infrastructure.Persistence`) necesita poder clasificar un fallo
sin depender de ningún paquete de broker concreto — la implementación específica de Kafka se
registra por composición (`AddSharedMessagingKafka`), no por referencia directa de proyecto.

## Por qué no se reutilizó `Microsoft.Extensions.Http.Resilience`/Polly directamente (F1-26)

`Shared.Infrastructure.Http` (F1-26, ver `docs/guia-resiliencia-http.md`) ya resuelve retry con
backoff exponencial y jitter para `HttpClient` saliente usando
`Microsoft.Extensions.Http.Resilience` (Polly v8). Se evaluó reutilizar el mismo paquete acá, pero
esa pipeline está diseñada para envolver la ejecución de un delegado **dentro de una operación
activa** (reintentar la misma llamada varias veces antes de devolver el control al llamador, con
un `Task.Delay` entre cada intento, todo dentro de la misma invocación síncrona/asíncrona).

El backoff de F3-07 necesita, del lado del relay de Outbox, lo opuesto: calcular **cuándo** debe
ocurrir el próximo ciclo de sondeo (una marca de tiempo futura persistida en
`OutboxMessage.LockedUntilUtc`), sin bloquear ningún hilo esperando ese tiempo — el ciclo actual
de `ProcessBatchAsync` debe poder seguir procesando el resto del lote de inmediato, y el próximo
intento de esta fila en particular ocurre recién en un ciclo de sondeo futuro (potencialmente
minutos después, ejecutado por la misma instancia o por otra réplica). Forzar esa espera con
`Task.Delay` real habría bloqueado el worker completo por cada fila que falla.

Agregar una dependencia nueva (`Polly.Core` directo, sin el resto de
`Microsoft.Extensions.Http.Resilience`) solo para una fórmula de backoff con jitter habría sido
una dependencia desproporcionada para el problema (`docs/politica-dependencias.md`: "no introducir
una dependencia sin revisar... compatibilidad" con el resto del diseño). `EventRetryBackoff`
implementa el mismo patrón estándar (exponencial + "full jitter", documentado por AWS
"Exponential Backoff And Jitter") con una función pura de `~15` líneas, sin ninguna dependencia
nueva.

Del lado consumidor (`KafkaEventConsumer<TEvent>`), el backoff SÍ termina siendo bloqueante
(`Task.Delay` real antes de relanzar la excepción) — ver la sección dedicada más abajo — pero
tampoco encaja con el modelo de "reintentar la misma llamada N veces" de Polly: acá cada llamada a
`ConsumeAndHandleOnceAsync` procesa como máximo un mensaje y retorna; el reintento real ocurre
porque Kafka reentrega el mismo mensaje en la SIGUIENTE invocación (potencialmente desde un loop
externo al que este proyecto no tiene acceso), no dentro de la misma invocación.

## Clasificación de errores (Kafka)

`KafkaEventPublishFailureClassifier.Classify` (usado tanto por el relay de Outbox contra
`ProduceException` como, con la salvedad de más abajo, del lado consumidor):

- `Error.IsFatal == true` → **Permanente**, sin importar el código (el productor/cliente quedó en
  un estado del que no se puede recuperar reintentando la misma operación).
- Código de error específico del mensaje/autorización (nunca cambia reintentando lo mismo) →
  **Permanente**: `MsgSizeTooLarge`, `InvalidMsg`, `InvalidMsgSize`, `RecordListTooLarge`,
  `UnsupportedForMessageFormat`, `InvalidRequiredAcks`, `TopicAuthorizationFailed`,
  `ClusterAuthorizationFailed`, `TopicException`.
- Cualquier otro código (`BrokerNotAvailable`, `RequestTimedOut`, `NetworkException`,
  `NotEnoughReplicas`, `Local_Transport`, `Local_TimedOut`, `Local_AllBrokersDown`, etc.) →
  **Transitorio**: condición del broker/red que puede resolverse sola con el paso del tiempo.
- Cualquier excepción que no sea `ProduceException` (por ejemplo, un fallo de serialización antes
  de llegar al broker) → **Transitorio** por defecto seguro: nunca se descarta un evento sin
  evidencia real de que reintentar sea inútil. `MaxAttempts` sigue acotando el reintento
  indefinido en cualquier caso.

Del lado consumidor, la excepción que falla es la del **handler de negocio**
(`IEventConsumer<TEvent>.ConsumeAsync` / `IInboxMessageProcessor.ProcessAsync`), casi nunca un
`ProduceException` — por eso `KafkaEventConsumer<TEvent>` usa por defecto
`DefaultEventPublishFailureClassifier` (todo transitorio) salvo que el proyecto consumidor
inyecte explícitamente un clasificador propio que sepa distinguir sus propias excepciones de
negocio "definitivamente inútiles de reintentar" de las que sí valen la pena reintentar.

## Backoff: exponencial con "full jitter"

`EventRetryBackoff.CalculateDelay(attemptNumber, options, random)`:

```
tope = min(options.MaxDelay, options.BaseDelay * 2^(attemptNumber - 1))
delay = random_uniforme(0, tope)          // "full jitter" (AWS)
```

- `attemptNumber` es 1-based: intento 1 usa `BaseDelay` como tope (`base * 2^0`), intento 2 usa
  `base * 2^1`, etc., hasta topar en `MaxDelay`.
- El jitter (aleatoriedad uniforme entre 0 y el tope) evita que múltiples filas/mensajes que
  fallaron al mismo tiempo (por ejemplo, todos por el mismo broker caído) reintenten exactamente
  en el mismo instante ("thundering herd") — mismo criterio que el jitter obligatorio de
  `Shared.Infrastructure.Http` (F1-26).
- `BaseDelay = TimeSpan.Zero` deshabilita el backoff (reintento inmediato) — usado en pruebas que
  necesitan determinismo sin depender de tiempos reales.

## Límite máximo y agotamiento

`EventRetryBackoff.IsExhausted(attemptsSoFar, options)` compara `attemptsSoFar >=
options.MaxAttempts`. Un mensaje se marca "agotado" cuando:

- El error se clasificó como `Permanent` (sin importar cuántos intentos queden disponibles — no
  tiene sentido esperar a agotar `MaxAttempts` para algo que ya se sabe que no va a funcionar), **o**
- El error es `Transient` pero `attemptsSoFar >= MaxAttempts`.

**Qué significa "agotado" en cada lado — nunca "perdido":**

| | Relay de Outbox | Consumidor Kafka |
|---|---|---|
| Señal | `OutboxMessage.ExhaustedAtUtc` (persistido en SQL Server) | `EventProcessingExhaustedException` (lanzada, no persistida) |
| Efecto inmediato | La fila deja de ser candidata en `ClaimBatchAsync` (filtro `ExhaustedAtUtc IS NULL`) | El offset de Kafka NO se confirma (igual que cualquier otra excepción no manejada) |
| ¿Se pierde el evento? | No — la fila sigue existiendo, consultable, con su `PayloadJson` intacto | No — Kafka sigue reteniendo/reentregando el mensaje (según retención del tópico) |
| Punto de extensión para F3-08 | Consultar `WHERE ExhaustedAtUtc IS NOT NULL` | Capturar específicamente `EventProcessingExhaustedException` en el host que invoca `ConsumeAndHandleOnceAsync` |

F3-07 deliberadamente **no** implementa ningún enrutamiento a una cola de mensajes muertos real —
esa es la tarea siguiente del backlog (F3-08, "DLQ"). F3-07 solo deja el estado/señal necesario
para que F3-08 pueda construir ese enrutamiento sin tener que re-descubrir "qué mensajes ya
fallaron demasiadas veces".

## Diferencia de diseño entre el relay de Outbox y el consumidor Kafka

| | Relay de Outbox (`OutboxBatchProcessor`) | Consumidor (`KafkaEventConsumer<TEvent>`) |
|---|---|---|
| Cómo se programa el próximo intento | `OutboxMessage.LockedUntilUtc` = ahora + backoff (marca de tiempo futura, sin bloquear nada) | `Task.Delay(backoff)` real ANTES de relanzar la excepción (bloqueante) |
| Dónde se guarda el conteo de intentos | `OutboxMessage.RetryCount` (persistido en SQL Server) | Diccionario en memoria por instancia del consumidor (`ConcurrentDictionary<string, int>`), NO persistido |
| Efecto de un reinicio del proceso sobre el conteo | Ninguno — el conteo sigue en la fila | Se pierde — el mensaje vuelve a tener margen completo de reintentos |
| Garantía de "nunca más de N intentos" | Dura (persistida) | "Mejor esfuerzo", solo dentro de la vida de un mismo proceso |

La asimetría es una limitación real de diseño, no un descuido: `ConsumeAndHandleOnceAsync` no
tiene loop propio (lo maneja el host que la invoca, ver `docs/guia-inbox-consumer.md`) y Kafka
reentrega el mismo mensaje en la siguiente llamada sin que este consumidor pueda decirle "esperá"
de otra forma que bloqueando la llamada actual. Persistir el conteo de intentos fallidos del lado
consumidor requeriría un cambio de esquema de Inbox (F1-24 solo persiste mensajes que terminaron
con éxito) — evaluado y descartado por exceder el alcance mínimo de F3-07 (Plan Maestro, sección
3.2, "cambio mínimo y cohesionado"); queda como extensión natural para F3-08/F3-09 si se necesita
una garantía dura también de este lado.

## Cómo configurar la política

**Relay de Outbox:**

```csharp
services.AddSharedOutboxPublisher(options =>
{
    options.Retry.MaxAttempts = 8;
    options.Retry.BaseDelay = TimeSpan.FromSeconds(3);
    options.Retry.MaxDelay = TimeSpan.FromMinutes(10);
});
```

**Consumidor Kafka** (parámetros opcionales del constructor, con los mismos defaults de
`EventRetryPolicyOptions` si se omiten):

```csharp
using var consumer = new KafkaEventConsumer<PedidoCreadoIntegrationEvent>(
    kafkaOptions,
    "Pedidos.PedidoCreado",
    scopeFactory,
    retryOptions: new EventRetryPolicyOptions { MaxAttempts = 5, BaseDelay = TimeSpan.FromSeconds(1) });
```

## Qué NO resuelve F3-07

- **DLQ real** (F3-08, ya implementado — ver `docs/runbook-dlq.md`): F3-07 solo dejó el estado/señal
  (`ExhaustedAtUtc`, `EventProcessingExhaustedException`) que F3-08 consume para el enrutamiento a un
  tópico de mensajes muertos y el reprocesamiento auditado.
- **Aislamiento de poison messages a nivel de tópico/partición** (F3-09).
- **Persistencia del conteo de intentos fallidos del lado consumidor** (ver tabla de arriba) — solo
  el relay de Outbox tiene una garantía dura de "nunca más de N intentos totales".
- **Observabilidad/métricas dedicadas** (F3-10): solo logging vía `ILogger`, sin
  contadores/histogramas de reintentos/agotamiento todavía.

## Pruebas

- `tests/Shared.Application.Tests/Eventing/EventRetryBackoffTests.cs` — cálculo puro de backoff
  (crecimiento exponencial, tope `MaxDelay`, rango del jitter, `IsExhausted`).
- `tests/Shared.Infrastructure.Messaging.Kafka.Tests/KafkaEventPublishFailureClassifierTests.cs` —
  clasificación por código de error/`IsFatal` de `ProduceException`, sin broker real.
- `tests/Shared.Infrastructure.Messaging.Kafka.Tests/KafkaEventPublisherBrokerUnavailableTests.cs` —
  la excepción real contra un broker inalcanzable se clasifica transitoria y su backoff cae dentro
  de rango.
- `tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxPublisherRetryTests.cs` —
  agotamiento inmediato ante error permanente, agotamiento tras `MaxAttempts` con error
  transitorio, y backoff real (`LockedUntilUtc` en el futuro) contra SQL Server real.
- `tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxPublisherIntegrationTests.cs` —
  actualizada para reflejar que un fallo ya no libera el lock a `null`, sino que programa
  `LockedUntilUtc` (con `BaseDelay = Zero` en ese test puntual, para mantener el reintento
  inmediato sin depender de tiempos reales).

## Referencias

- `docs/runbook-dlq.md` (F3-08, DLQ y reprocesamiento auditado — el consumidor directo de esta política).
- `docs/guia-outbox-publisher.md` (F3-03/F3-07, relay de Outbox completo).
- `docs/guia-inbox-consumer.md` (F3-04/F3-07, consumidor Kafka completo).
- `docs/guia-resiliencia-http.md` (F1-26, patrón de referencia del lado HTTP saliente).
- `docs/politica-dependencias.md` (por qué no se agregó `Polly.Core` directo).
- `docs/plan-maestro-bitcode-ia.md` (Fase 3, fila F3-07; sección 3.2, regla dura contra prometer
  exactly-once de punta a punta).
- ADR `docs/adr/0005-mensajeria-kafka.md` (`Accepted`).
