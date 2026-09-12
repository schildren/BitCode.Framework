# Runbook — Dead-Letter Queue (DLQ) de eventos de integración (F3-08/F3-09)

**Tarea:** F3-08 (Fase 3 — Plataforma de eventos) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md),
extendido por F3-09 (aislamiento de poison messages, sección dedicada más abajo).
**Trabajo:** DLQ — crear dead-letter topics, metadatos y reprocess (F3-08); aislar mensajes inválidos sin
bloquear la partición (F3-09).
**Entregable:** Runbook y tooling (F3-08); handler de aislamiento (F3-09).
**Criterio de aceptación:** "Reprocesamiento auditado" (F3-08); "Consumer continúa operando" (F3-09).

## Objetivo

F3-07 dejó dos señales de "este mensaje agotó su margen de reintentos" sin ningún enrutamiento a una
cola de mensajes muertos real:

- Lado emisor (relay de Outbox): `OutboxMessage.ExhaustedAtUtc` (persistido, consultable por SQL).
- Lado consumidor (`KafkaEventConsumer<TEvent>`): `EventProcessingExhaustedException` (lanzada, sin
  persistir nada).

F3-08 cierra ese vacío con dos piezas:

1. **Tooling (Mitad 1 — dead-letter topics y metadatos):** ambos lados publican, best-effort, una copia
   del mensaje agotado a un tópico Kafka `{tópico original}.dlq` con metadatos enriquecidos en headers
   (motivo, intentos, timestamp de agotamiento, tópico/evento original) — un operador que solo inspecciona
   Kafka puede filtrar/decidir sin necesitar la base de datos.
2. **Tooling + este runbook (Mitad 2 — reprocesamiento auditado):** `IDeadLetterReprocessor`
   (`Shared.Infrastructure.Security.DeadLetter`) reabre una fila agotada de `OutboxMessage` y deja un
   registro de auditoría (`IAuditWriter`, Épica F2-D) de quién lo pidió, cuándo y por qué.

## Dónde vive cada pieza

| Componente | Proyecto | Rol |
|---|---|---|
| `IDeadLetterPublisher` / `DeadLetterEnvelope` | `Shared.Application.Eventing` | Contrato agnóstico de broker (mismo criterio que `IEventPublisher`, F3-01). |
| `KafkaDeadLetterPublisher` | `Shared.Infrastructure.Messaging.Kafka` | Publica a `{tópico}.dlq` con headers `bitcode-dlq-*`. |
| `OutboxBatchProcessor` | `Shared.Infrastructure.Persistence` | Invoca `IDeadLetterPublisher` inmediatamente DESPUÉS de persistir `OutboxMessage.ExhaustedAtUtc` (nunca antes). |
| `KafkaEventConsumer<TEvent>` | `Shared.Infrastructure.Messaging.Kafka` | Al capturar `EventProcessingExhaustedException`, publica a DLQ y confirma el offset (quarantine). |
| `IDeadLetterReprocessor` / `OutboxDeadLetterReprocessor` | `Shared.Infrastructure.Security.DeadLetter` | Reabre `OutboxMessage` + audita la operación (`IAuditWriter`, F2-15). |
| `AddSharedDeadLetterReprocessing` | `Shared.Infrastructure.Security.DeadLetter` | Registro DI del reprocesador. |

### Por qué el reprocesador vive en `Shared.Infrastructure.Security`, no en `Shared.Infrastructure.Persistence`

`OutboxBatchProcessor` (el componente que ya sabe reclamar/reintentar filas de `OutboxMessage`) vive en
`Shared.Infrastructure.Persistence`. El reprocesamiento auditado necesita, además de `OutboxMessage`,
`IAuditWriter` (`Shared.Infrastructure.Security`, Épica F2-D). `Shared.Infrastructure.Security` **ya**
referencia `Shared.Infrastructure.Persistence` (para `MultiTenantIdentityDbContext`, F2-01) — la
referencia inversa (`Shared.Infrastructure.Persistence` → `Shared.Infrastructure.Security`) crearía un
ciclo de proyectos. Por eso `IDeadLetterReprocessor`/`OutboxDeadLetterReprocessor` viven junto al resto
del módulo de seguridad (`Shared.Infrastructure.Security/DeadLetter`), no junto al relay.

### Naming de tópicos dead-letter

`{tópico original resuelto por IKafkaTopicNameResolver}.dlq` — un tópico DLQ por tipo de evento, no un
único tópico DLQ global compartido por todos los bounded contexts. Mismo criterio de aislamiento que ya
usa el esquema de tópicos "feliz" (F3-02): retención, permisos (F3-11) y herramientas de reprocesamiento
pueden operar por tipo de evento sin filtrar un tópico mezclado.

### Headers del mensaje DLQ (`KafkaDeadLetterPublisher`)

| Header | Contenido |
|---|---|
| `bitcode-dlq-original-event-type` | `EventType` original (lógico si se conoce, o el técnico si no — ver `DeadLetterEnvelope.EventType`). |
| `bitcode-dlq-original-topic` | Tópico "feliz" del que se derivó el tópico DLQ. |
| `bitcode-dlq-reason` | Motivo del último fallo (mensaje de la excepción). |
| `bitcode-dlq-attempts` | Cantidad total de intentos realizados. |
| `bitcode-dlq-exhausted-at-utc` | Timestamp UTC (`O`) de agotamiento. |
| `bitcode-dlq-source-message-id` | `OutboxMessage.Id` (lado emisor) o `IIntegrationEvent.EventId` (lado consumidor), si está disponible. |

El `Value` del mensaje DLQ es el payload original SIN transformar (mismos bytes que se intentaron
publicar/procesar) — la copia de conveniencia nunca reemplaza al payload real.

## Semántica de "best-effort" (por qué la publicación a DLQ nunca es la fuente de verdad)

`OutboxMessage` sigue siendo la única fuente de verdad de que un mensaje se agotó del lado emisor
(F3-03, "reinicio no pierde eventos"): la publicación a DLQ ocurre DESPUÉS de persistir
`ExhaustedAtUtc`, y si falla (broker caído, tópico sin permisos) solo se registra un warning — el ciclo
de `OutboxBatchProcessor.ProcessBatchAsync` sigue procesando el resto del lote. Un operador siempre
puede recuperar los mensajes agotados consultando `OutboxMessage` directamente, con o sin la copia en
Kafka.

Del lado consumidor, al agotar el margen se publica a DLQ y **se confirma el offset** — a diferencia de
F3-07 (donde el offset nunca se confirmaba y Kafka reentregaba el mismo mensaje sin fin, bloqueando el
resto de la partición). Esto resuelve el caso puntual de aislamiento para mensajes ya explícitamente
agotados; el aislamiento general de cualquier mensaje que ni siquiera se puede deserializar (poison
message) es F3-09 (ver sección dedicada más abajo).

## Procedimiento operativo

### 1. Identificar mensajes en DLQ

**Opción A — consultar `OutboxMessage` directamente (recomendada, siempre disponible):**

```sql
SELECT Id, TenantId, EventType, Error, RetryCount, ExhaustedAtUtc, OccurredAtUtc
FROM OutboxMessage
WHERE ExhaustedAtUtc IS NOT NULL
ORDER BY ExhaustedAtUtc DESC;
```

**Opción B — inspeccionar el tópico Kafka `{tópico}.dlq`** (copia de conveniencia, puede faltar si la
publicación a DLQ falló): consumir el tópico con cualquier herramienta estándar de Kafka
(`kafka-console-consumer`, un consumidor ad hoc) y leer los headers `bitcode-dlq-*` para filtrar sin
deserializar el payload completo.

### 2. Decidir: ¿reprocesar o descartar definitivamente?

- **Reprocesar** si la causa raíz ya se corrigió (el broker volvió a estar disponible, se corrigió un bug
  de deserialización, se ajustaron permisos de tópico, etc.) y el efecto de negocio todavía es válido.
- **Descartar** (no reprocesar) si el evento ya no es relevante (por ejemplo, el pedido que originó el
  evento fue cancelado por otra vía) — F3-08 no implementa un "descarte explícito" con su propio estado;
  descartar significa simplemente NO llamar a `ReprocessAsync` (la fila queda agotada, consultable,
  para siempre — nunca se pierde ni se borra).

### 3. Ejecutar el reprocesamiento

```csharp
var reprocessor = serviceProvider.GetRequiredService<IDeadLetterReprocessor>();

var result = await reprocessor.ReprocessAsync(new DeadLetterReprocessRequest(
    outboxMessageId: outboxMessageId,          // Id de la fila identificada en el paso 1
    actor: new AuditActor(operatorId, AuditActorType.User),
    reason: "Causa raíz corregida: permisos de tópico Kafka restaurados (INC-1234)."));

if (result.IsFailure)
{
    // result.Error.Code: "DeadLetter.OutboxMessageNotFound" | "DeadLetter.NotExhausted" | error de auditoría
}
```

`ReprocessAsync`:

1. Busca el `OutboxMessage` por Id (`IgnoreQueryFilters`, es una operación de plataforma, no atada al
   tenant del scope actual).
2. Si no existe → `Result.Failure` (`DeadLetter.OutboxMessageNotFound`).
3. Si no está agotado (`ExhaustedAtUtc` es `null`) → `Result.Failure` (`DeadLetter.NotExhausted`).
4. Escribe la entrada de auditoría (`IAuditWriter.WriteAsync`, acción `OutboxMessage.DeadLetterReprocess`)
   **ANTES** de tocar la fila — si la auditoría falla, la fila **nunca** se reabre (ver remarks de
   `OutboxDeadLetterReprocessor` para el análisis completo del trade-off de este orden).
5. Resetea `ExhaustedAtUtc`/`Error`/`LockedUntilUtc`/`LockedBy` a `null` y `RetryCount` a `0` (margen
   completo de reintentos otra vez — una decisión consciente del operador, no continuación del conteo
   agotado).
6. Guarda los cambios; el próximo ciclo de sondeo de `OutboxBatchProcessor` vuelve a reclamar la fila.

### 4. Verificar que quedó auditado

Consultar la auditoría por `Resource.Type = "OutboxMessage"` y `Resource.Id = <outboxMessageId>` (por
ejemplo, vía `IAuditQueryService`/`IAuditReader`, F2-20) y confirmar que existe una entrada con
`Action = "OutboxMessage.DeadLetterReprocess"`, el actor esperado y el motivo (`Reason`) documentado.
`DeadLetterReprocessResult.AuditEntryId` (el `Guid` devuelto por `ReprocessAsync` en caso de éxito) es el
Id exacto de esa entrada.

## Registro (DI)

```csharp
services.AddSharedPersistence<MiDbContext>(connectionString);
services.AddSharedMessagingKafka(configuration);   // registra IDeadLetterPublisher (KafkaDeadLetterPublisher)
services.AddSharedOutboxPublisher();               // OutboxBatchProcessor toma IDeadLetterPublisher por DI (opcional)
services.AddSharedAuditing();                      // IAuditWriter (InMemoryAuditWriter por defecto; reemplazar por una implementación persistente real en producción)
services.AddSharedDeadLetterReprocessing();         // IDeadLetterReprocessor
```

Orden relevante: `AddSharedMessagingKafka` ANTES de `AddSharedOutboxPublisher` (mismo criterio ya
documentado para el clasificador de fallos, F3-07) para que `IDeadLetterPublisher` esté disponible
cuando `OutboxBatchProcessor` lo resuelve; si se omite, `OutboxBatchProcessor` sigue funcionando (el
parámetro es opcional, `null` por defecto) — solo no publica copias a DLQ, la fila se agota igual.

## Aislamiento de poison messages (F3-09)

F3-08 resolvió el aislamiento del caso puntual "mensaje ya explícitamente agotado" (F3-07). F3-09 cierra
el caso general: un mensaje que `KafkaEventConsumer<TEvent>` **ni siquiera puede deserializar** a su
`TEvent` (JSON corrupto, forma incompatible, payload de otro esquema) — un error del MENSAJE, no del
handler de negocio, y por definición PERMANENTE (reintentarlo nunca lo arregla, porque nunca llega a
ejecutarse ningún handler).

**Diferencia de tratamiento respecto de F3-07/F3-08** (detalle completo en
`docs/guia-inbox-consumer.md`, sección "Poison messages vs. agotamiento de reintentos"): un poison message
se detecta ANTES de abrir el scope de DI / invocar Inbox, nunca pasa por
`IEventPublishFailureClassifier` ni por el backoff de F3-07, y se publica a DLQ con un único intento (no
hay "agotamiento de margen" porque nunca hubo margen que gastar). El offset se confirma siempre —
incluida la publicación a DLQ fallida, mismo criterio best-effort ya descrito arriba.

**Cómo distinguir un registro DLQ de poison message de uno de agotamiento de reintentos:** el header
`bitcode-dlq-reason` (o `DeadLetterEnvelope.Reason`) de un poison message siempre empieza con el prefijo
`"PoisonMessage"` (por ejemplo, `"PoisonMessage (DeserializationFailure): ..."`), a diferencia del motivo
de un mensaje agotado, que es el mensaje de la excepción de negocio original.

**Reprocesamiento de un poison message:** no aplica el mismo camino que `IDeadLetterReprocessor`
(pensado para `OutboxMessage.ExhaustedAtUtc`, que no existe para este caso — un poison message nunca pasó
por el relay de Outbox del lado emisor de ESTE consumidor, es un mensaje ya recibido con una forma
inválida). La única forma de "reprocesar" un poison message real es corregir la causa raíz (por ejemplo,
un productor externo que empezó a emitir una forma de evento distinta) y republicar manualmente una
versión corregida del payload al tópico original — no hay automatismo de este framework para eso, mismo
límite ya documentado para el reprocesamiento del lado consumidor en general (ver "Qué NO resuelve F3-08"
más abajo).

## Qué NO resuelve F3-08

- **Herramienta de operador con interfaz propia** (UI/CLI dedicada): el "tooling" de esta tarea es la
  API (`IDeadLetterReprocessor`) más este runbook con comandos SQL/C# — no se construyó ninguna interfaz
  gráfica ni CLI empaquetada; un proyecto consumidor puede envolver `IDeadLetterReprocessor` en su propio
  endpoint administrativo (protegido con RBAC/ABAC, F2-07/F2-08, y `PrivilegedOperationsOptions`, Épica
  F2-C) si lo necesita.
- **Reprocesamiento del lado consumidor** (`KafkaEventConsumer<TEvent>`): al agotar el margen, el
  consumidor publica a DLQ y confirma el offset, pero no existe un `IDeadLetterReprocessor` simétrico que
  reinyecte el mensaje al tópico "feliz" original — el mecanismo de reprocesamiento auditado (Mitad 2)
  cubre el lado emisor (`OutboxMessage`), que es la fuente de verdad persistida y con garantía dura de
  "nunca más de N intentos" (F3-07). Reprocesar del lado consumidor implicaría republicar manualmente el
  mensaje leído del tópico `.dlq` al tópico original (`IEventPublisher`/`KafkaEventPublisher`), y auditar
  esa acción con el mismo criterio — queda como extensión natural si un proyecto consumidor lo necesita,
  no como automatismo de este framework.
- **Observabilidad/métricas dedicadas de DLQ** (contadores de mensajes en DLQ, alertas) — F3-10.
- **Descarte explícito con su propio estado/endpoint** — ver paso 2 del procedimiento operativo.

## Pruebas

- `tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxDeadLetterIntegrationTests.cs` —
  criterio de aceptación literal de F3-08 ("Reprocesamiento desde DLQ"): SQL Server + Kafka reales
  (Testcontainers). Un publisher forzado a fallar permanentemente agota la fila
  (`OutboxMessage.ExhaustedAtUtc`), se verifica la copia en el tópico `.dlq` (headers `bitcode-dlq-*`),
  se reprocesa con `IDeadLetterReprocessor` y se verifica que (a) existe la entrada de auditoría
  correspondiente y (b) la fila vuelve a ser candidata de publicación (`ExhaustedAtUtc` vuelve a `null`,
  reclamable por `OutboxBatchProcessor` en el siguiente ciclo).
- `tests/Shared.Infrastructure.Messaging.Kafka.Tests/KafkaDeadLetterPublisherTests.cs` — el tópico
  dead-letter y los headers se resuelven/serializan correctamente contra un broker Kafka real.
- `tests/Shared.Infrastructure.Security.Tests/DeadLetter/OutboxDeadLetterReprocessorTests.cs` —
  `NotFound`/`Conflict` cuando la fila no existe o no está agotada, y que un fallo de `IAuditWriter`
  impide reabrir la fila (orden "auditar primero, mutar después").
- `tests/Shared.Infrastructure.Messaging.Kafka.Tests/Integration/KafkaEventConsumerPoisonMessageIntegrationTests.cs`
  (F3-09) — criterio de aceptación literal ("Consumer continúa operando"): contra Kafka real, publica un
  mensaje poison (bytes no-JSON) seguido de un mensaje válido; verifica que la primera llamada a
  `ConsumeAndHandleOnceAsync` aísla el poison message (sin lanzar sin manejo, offset confirmado) y que la
  siguiente llamada procesa con normalidad el mensaje válido — la partición no queda bloqueada. Verifica
  también que el mensaje poison llega al tópico `.dlq` con el motivo `"PoisonMessage"`.

## Referencias

- `docs/politica-reintentos-eventos.md` (F3-07, estado/señal de agotamiento que F3-08 consume).
- `docs/guia-outbox-publisher.md` (F3-03/F3-07, relay de Outbox completo).
- `docs/guia-inbox-consumer.md` (F3-04/F3-07/F3-09, consumidor Kafka completo, incluida la tabla
  poison message vs. agotamiento de reintentos).
- `docs/guia-auditoria-inmutable.md` (F2-15/F2-16, Épica F2-D).
- `docs/plan-maestro-bitcode-ia.md` (Fase 3, filas F3-08/F3-09; Épica F2-C, "operaciones privilegiadas").
- ADR `docs/adr/0005-mensajeria-kafka.md` (`Accepted`).
