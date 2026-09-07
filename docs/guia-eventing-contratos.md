# Contratos de eventos de integración (F3-01)

## Objetivo

Fase 3 ("Plataforma de eventos") necesita un paquete de contratos agnóstico de proveedor sobre el
cual construir, en tareas posteriores, el adapter Kafka (F3-02), el relay de Outbox (F3-03) y el
consumer base de Inbox (F3-04). Esta tarea (F3-01) define ese paquete: `IIntegrationEvent`,
`IEventPublisher` e `IEventConsumer<TEvent>`, todos en `BitCode.Framework.Shared.Application.Eventing`
(`Shared.Application`).

Criterio de aceptación: **sin dependencia al proveedor**. `Shared.Application` no referencia ningún
paquete de broker (Kafka u otro) — los tres contratos son interfaces .NET puras, sin ningún tipo del
SDK de un broker concreto en su firma.

## `IIntegrationEvent` vs `DomainEvent`

No son el mismo concepto, aunque ambos representan "algo que pasó":

| | `DomainEvent` (Shared.Kernel, F1-23) | `IIntegrationEvent` (Shared.Application, F3-01) |
|---|---|---|
| Alcance | Interno al agregado/bounded context que lo levanta | Cruza el límite de un bounded context — contrato PÚBLICO |
| Cómo se genera | `AggregateRoot<TId>.RaiseDomainEvent(...)` dentro de un método de negocio | Reconstruido por el relay de Outbox (F3-03) a partir de una fila `OutboxMessage`, o recibido de un broker externo |
| Persistencia | Fila `OutboxMessage`, `EventType` = `Type.AssemblyQualifiedName` del tipo .NET | Serializado al payload que via a un tópico externo; `EventType` es un nombre lógico y estable (`"Pedidos.PedidoCreado"`), no el nombre del tipo .NET |
| Versión de esquema | No aplica (nunca sale del proceso) | `SchemaVersion` explícito (ver `docs/politica-versionado.md`) |

Cambiar la forma de un `IIntegrationEvent` es un cambio de contrato público sujeto a
`docs/politica-versionado.md`; cambiar un `DomainEvent` interno no lo es (nunca lo consume nadie fuera
del propio proceso).

## Contratos

### `IIntegrationEvent`

```csharp
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredOnUtc { get; }
    string EventType { get; }
    int SchemaVersion { get; }
}
```

- `EventId`: identificador único de esta instancia del evento — clave natural de deduplicación del
  lado consumidor (Inbox, F1-24/F3-04).
- `OccurredOnUtc`: momento en que ocurrió el hecho de negocio, no el momento de publicación/consumo.
- `EventType`: nombre lógico y estable usado para enrutamiento/nombre de tópico.
- `SchemaVersion`: versión explícita del esquema (política de versionado de eventos, F3-06 define la
  compatibilidad forward/backward concreta sobre este campo; F3-01 solo reserva el campo).

`IntegrationEvent` (mismo namespace) es una base `abstract record` de conveniencia — mismo patrón que
`DomainEvent` de Shared.Kernel — que resuelve `EventId`/`OccurredOnUtc` con un valor por defecto al
construirse, y expone `SchemaVersion` con default `1` (override si el evento introduce un cambio
incompatible de forma). No es obligatorio heredar de ella: un evento reconstruido por deserialización
que necesita preservar el `EventId`/`OccurredOnUtc` originales del productor puede implementar
`IIntegrationEvent` directamente.

```csharp
public sealed record PedidoCreadoIntegrationEvent(Guid PedidoId, string Cliente) : IntegrationEvent
{
    public override string EventType => "Pedidos.PedidoCreado";
}
```

### `IEventPublisher`

```csharp
public interface IEventPublisher
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
    Task PublishAsync(IEnumerable<IIntegrationEvent> integrationEvents, CancellationToken cancellationToken = default);
}
```

Pensado como el punto de enganche que el relay de Outbox (F3-03) invoca DESPUÉS de leer un lote de
filas `OutboxMessage` pendientes (`ProcessedAtUtc IS NULL`) y reconstruir el `IIntegrationEvent`
correspondiente — nunca dentro de la transacción de negocio que originó el evento (regla dura 3 de
`docs/convenciones.md`). Un handler de comando **nunca** inyecta ni llama a `IEventPublisher`
directamente: la vía correcta sigue siendo `AggregateRoot<TId>.RaiseDomainEvent` (F1-23) + el relay de
Outbox.

F3-01 no incluye ninguna implementación de `IEventPublisher` — es responsabilidad de F3-02 (adapter
Kafka) en adelante.

### `IEventConsumer<TEvent>`

```csharp
public interface IEventConsumer<in TEvent> where TEvent : IIntegrationEvent
{
    Task ConsumeAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
```

Pensado para ser invocado como el `handler` que un futuro consumidor real (F3-02/F3-04) pasa a
`IInboxMessageProcessor.ProcessAsync` (F1-24) — nunca se ejecuta el efecto de negocio del evento "a
mano" fuera de ese mecanismo. `ConsumeAsync` debe modificar el estado a través del mismo
`DbContext`/`IUnitOfWork` de scope sin llamar a `SaveChangesAsync` por su cuenta, igual que cualquier
`handler` de Inbox.

La entrega es "al menos una vez" y el procesamiento debe ser idempotente (semántica exigida de la Fase
3, Plan Maestro): un `IEventConsumer<TEvent>` no puede asumir que `ConsumeAsync` se invoca exactamente
una vez por evento — la deduplicación real la da el Inbox (F1-24/F3-04), no este contrato.

## Qué NO resuelve F3-01

- Ninguna implementación concreta de `IEventPublisher`/`IEventConsumer<TEvent>` (adapter Kafka, F3-02).
- Ningún worker que lea `OutboxMessage`/escriba `InboxMessage` a partir de estos contratos (F3-03/F3-04).
- Particionamiento (F3-05), compatibilidad de esquema (F3-06), reintentos (F3-07), DLQ (F3-08),
  poison messages (F3-09), observabilidad (F3-10), seguridad de transporte (F3-11) ni catálogo de
  eventos (F3-12).
- La introducción operativa de Kafka como broker productivo sigue condicionada a la aprobación humana
  de ADR `docs/adr/0005-mensajeria-kafka.md` (Plan Maestro, sección 13) — F3-01 no la requiere porque
  no introduce ningún broker, solo contratos .NET.

## Adapter Kafka (F3-02)

ADR `docs/adr/0005-mensajeria-kafka.md` pasó a `Accepted` el 2026-09-06 (aprobación humana explícita,
sección 13 del Plan Maestro, alcance: desarrollo/CI/pruebas de integración con broker real no
productivo). F3-02 agrega `src/Shared.Infrastructure.Messaging.Kafka`, la primera implementación
concreta de los contratos de F3-01:

- **`KafkaMessagingOptions`** (sección de configuración `"Messaging:Kafka"`): `BootstrapServers`,
  `ClientId`, `ConsumerGroupId`, y el punto de extensión de autenticación/transporte
  (`SecurityProtocol`, por defecto `Plaintext`; `SaslMechanism`/`SaslUsername`/`SaslPassword`/
  `SslCaLocation`) — SASL/SSL productivo es F3-11, no implementado ni validado por esta tarea (el
  único modo probado contra un broker real es `Plaintext`, vía `KafkaContainerFixture`).
- **`KafkaClientConfigFactory`**: traduce `KafkaMessagingOptions` a `ProducerConfig`/`ConsumerConfig`
  de `Confluent.Kafka` en un único lugar, para que productor y consumidor compartan siempre la misma
  configuración de transporte. El `ConsumerConfig` fuerza `EnableAutoCommit = false` — ver más abajo.
- **`IKafkaTopicNameResolver`**/`DefaultKafkaTopicNameResolver`: resuelve el nombre de tópico a partir
  de `IIntegrationEvent.EventType` (por ahora, tal cual, con caracteres no válidos reemplazados por
  `_`). Punto de extensión explícito para F3-05 (particionamiento/estrategia de tópicos por
  tenant/ambiente) — F3-02 no lo resuelve más allá de este mínimo.
- **`KafkaIntegrationEventSerializer`**: serializa el evento a JSON del tipo .NET concreto
  (`JsonSerializer.SerializeToUtf8Bytes(evento, evento.GetType())`, mismo criterio que
  `OutboxSaveChangesInterceptor` para `DomainEvent`) como `Value` del mensaje, y repite
  `EventType`/`SchemaVersion`/`EventId`/`OccurredOnUtc` como headers Kafka (`bitcode-event-type`,
  `bitcode-schema-version`, `bitcode-event-id`, `bitcode-occurred-on-utc`) para que un futuro
  consumidor de observabilidad (F3-10) los lea sin deserializar el payload completo.
- **`KafkaEventPublisher : IEventPublisher`**: recibe un `IProducer<string, byte[]>` ya construido
  (compartido, thread-safe) y publica cada evento al tópico resuelto por `EventType`, con `Key` =
  `EventId` (placeholder razonable hasta que F3-05 defina la clave de partición definitiva).
- **`KafkaEventConsumer<TEvent> : IDisposable`**: se suscribe al tópico de un `EventType` concreto y,
  por cada mensaje, deserializa a `TEvent` y lo procesa de forma deduplicada vía
  `IInboxMessageProcessor.ProcessAsync` (F1-24/F3-04) — **solo si termina sin excepción** (procesado o
  descartado por duplicado) confirma el offset (`Commit`). Si `ConsumeAsync`/`ProcessAsync` lanza, el
  offset no se confirma: Kafka reentrega el mismo mensaje (at-least-once), y esa reentrega vuelve a pasar
  por Inbox, que la descarta sin re-ejecutar el efecto si ya quedó marcada como procesada. Ver
  `docs/guia-inbox-consumer.md` (F3-04) para el detalle completo de esta coordinación.
- **`AddSharedMessagingKafka(configuration)`**: registra `IEventPublisher` → `KafkaEventPublisher` y el
  `IProducer<string, byte[]>` compartido. No registra ningún `KafkaEventConsumer<TEvent>` (cada
  bounded context lo instancia explícitamente, atado a su propio `IEventConsumer<TEvent>`).

Pruebas: `tests/Shared.Infrastructure.Messaging.Kafka.Tests/Integration/KafkaEventPublisherConsumerIntegrationTests.cs`
verifica el round-trip productor→consumidor contra un broker Kafka real (`KafkaContainerFixture`,
`Shared.Testing`, imagen `confluentinc/cp-kafka:6.1.9` — ver `docs/matriz-soporte.md`).
`KafkaEventPublisherBrokerUnavailableTests` cubre, sin Testcontainers, que `PublishAsync` falla con la
excepción del cliente de Kafka (no la absorbe en silencio) cuando el broker configurado no responde —
la clasificación de error transitorio/permanente y el backoff con reintentos es F3-07, no esta tarea.

## Qué NO resuelve F3-02

- Integración con `OutboxMessage` (relay real de Outbox, agregado después por F3-03 — ver sección más
  abajo). La integración con `InboxMessage`/deduplicación real quedó cerrada por F3-04 (ver más abajo).
- Particionamiento definitivo (F3-05), compatibilidad de esquema (F3-06), retries con backoff (F3-07),
  DLQ (F3-08), poison messages (F3-09), observabilidad/métricas (F3-10), seguridad de transporte real
  con SASL/SSL (F3-11) ni catálogo de eventos (F3-12).
- Habilitar tráfico productivo sobre Kafka: sigue condicionado a una aprobación humana adicional en el
  momento de ese despliegue (ADR `docs/adr/0005-mensajeria-kafka.md`, adenda de F3-02).

## Relay de Outbox (F3-03)

F3-03 agrega `OutboxBatchProcessor`/`OutboxPublisherBackgroundService`
(`Shared.Infrastructure.Persistence.Outbox`): el worker que lee por lotes las filas `OutboxMessage`
pendientes (F1-23), reconstruye el `IIntegrationEvent` correspondiente a cada una y lo publica vía
`IEventPublisher` (F3-02) — el "relay" que las tareas anteriores dejaron pendiente. El detalle completo
del mecanismo de bloqueo entre réplicas y, sobre todo, la decisión de diseño del mapeo
`OutboxMessage` → `IIntegrationEvent` (la pieza más delicada de F3-03) están documentados en
`docs/guia-outbox-publisher.md` — este archivo solo resume el resultado:

- Un `DomainEvent` (Shared.Kernel) que además implementa `IIntegrationEvent` en el mismo `record` se
  publica tal cual al llegar su turno en el relay. Un `DomainEvent` que NUNCA implementó
  `IIntegrationEvent` es puramente interno al bounded context: el relay lo marca como procesado sin
  publicar nada.
- `services.AddSharedOutboxPublisher(configureOptions?)` (`Shared.Infrastructure.Persistence.Outbox`),
  llamado DESPUÉS de `AddSharedPersistence<TContext>` y de registrar un `IEventPublisher` concreto (por
  ejemplo, `AddSharedMessagingKafka`), registra el `BackgroundService` que corre el relay en loop.

Pruebas: `tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxPublisherIntegrationTests.cs`
verifica, contra SQL Server real y Kafka real, el criterio de aceptación "reinicio no pierde eventos"
(caída antes de publicar, caída después de publicar pero antes de marcar — duplicado aceptable pero
nunca una fila huérfana sin publicar) y que dos instancias concurrentes del worker nunca publican la
misma fila dos veces.

## Inbox Consumer (F3-04)

F3-04 cierra la limitación que F3-02 dejó pendiente: `KafkaEventConsumer<TEvent>.ConsumeAndHandleOnceAsync`
ahora coordina con `IInboxMessageProcessor` (F1-24) — cada mensaje entregado por Kafka pasa por
`ProcessAsync(EventId, ...)` ANTES de confirmar el offset, así que una reentrega (rebalance, reinicio del
consumidor, o el duplicado aceptable que puede introducir el relay de Outbox, F3-03) nunca vuelve a
ejecutar el efecto de negocio. Cambio de contrato asociado: el constructor de `KafkaEventConsumer<TEvent>`
ya no recibe un `IEventConsumer<TEvent>` construido de antemano, sino un `IServiceScopeFactory` — crea un
scope de DI nuevo por mensaje, del que resuelve tanto `IInboxMessageProcessor` como
`IEventConsumer<TEvent>` (ambos necesitan compartir el mismo `DbContext`/`IUnitOfWork` de scope para que
la fila de Inbox y el efecto de negocio se persistan atómicamente). Detalle completo, incluida la guía de
registro en DI y las pruebas contra SQL Server + Kafka reales: `docs/guia-inbox-consumer.md`.

## Referencias

- `src/Shared.Application/Eventing/IIntegrationEvent.cs`, `IntegrationEvent.cs`, `IEventPublisher.cs`, `IEventConsumer.cs`.
- `tests/Shared.Application.Tests/Eventing/EventingContractsTests.cs`, `TestIntegrationEvents.cs`.
- `src/Shared.Infrastructure.Messaging.Kafka/` (F3-02): `KafkaMessagingOptions.cs`, `KafkaClientConfigFactory.cs`, `IKafkaTopicNameResolver.cs`, `KafkaIntegrationEventSerializer.cs`, `KafkaEventPublisher.cs`, `KafkaEventConsumer.cs`, `KafkaServiceCollectionExtensions.cs`.
- `src/Shared.Infrastructure.Persistence/Outbox/` (F3-03): `OutboxBatchProcessor.cs`, `OutboxBatchResult.cs`, `OutboxPublisherOptions.cs`, `OutboxPublisherBackgroundService.cs`, `OutboxPublisherServiceCollectionExtensions.cs`. Ver `docs/guia-outbox-publisher.md` para el detalle completo.
- `src/Shared.Testing/KafkaContainerFixture.cs` y `tests/Shared.Infrastructure.Messaging.Kafka.Tests/`.
- `tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxPublisherIntegrationTests.cs` (F3-03).
- `docs/guia-inbox-consumer.md` (F3-04, coordinación Kafka + Inbox), `tests/Shared.Infrastructure.Persistence.Tests/Integration/InboxConsumerIntegrationTests.cs`.
- `docs/convenciones.md` (regla dura 17/18/22/23, Outbox/Inbox/adapter Kafka/relay de Outbox, F1-23/F1-24/F3-02/F3-03).
- `docs/matriz-soporte.md` (imagen de Kafka usada en test, brechas de SASL/SSL/Inbox).
- `docs/politica-dependencias.md` (evaluación de `Confluent.Kafka`/`Testcontainers.Kafka`).
- `docs/politica-versionado.md` (versión de esquema de eventos de integración).
- ADR `docs/adr/0005-mensajeria-kafka.md` (`Accepted` desde F3-02).
