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
  `IHasPartitionKey.PartitionKey` si el evento la implementa, o `EventId` en caso contrario (F3-05, ver
  sección más abajo).
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
- Particionamiento definitivo, cerrado después por F3-05 (ver sección más abajo). Compatibilidad de
  esquema (F3-06), retries con backoff (F3-07), DLQ (F3-08), poison messages (F3-09),
  observabilidad/métricas (F3-10), seguridad de transporte real con SASL/SSL (F3-11) ni catálogo de
  eventos (F3-12).
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

## Particionamiento (F3-05)

F3-05 cierra el punto de extensión que F3-02 dejó explícitamente pendiente (`Key` del mensaje Kafka):
agrega `IHasPartitionKey` (`Shared.Application.Eventing`), una interfaz OPCIONAL que un
`IIntegrationEvent` concreto puede implementar para declarar su clave de partición explícita.

```csharp
public interface IHasPartitionKey
{
    string PartitionKey { get; }
}
```

- **Por qué opcional, no un campo de `IIntegrationEvent`:** distintos eventos necesitan distinto
  criterio de orden (o ninguno). Agregar un campo obligatorio a `IIntegrationEvent` habría sido un
  cambio de contrato público retroactivo sobre F3-01, rompiendo cualquier evento ya escrito entre F3-01
  y F3-04. `IHasPartitionKey` es una interfaz adicional: un evento que no la implementa sigue
  compilando y comportándose exactamente igual que antes de F3-05.
- **`KafkaEventPublisher.PublishAsync`** (F3-02, actualizado por F3-05): si el `IIntegrationEvent` que
  recibe implementa `IHasPartitionKey`, usa `PartitionKey` como `Key` del mensaje Kafka. Si no la
  implementa, sigue usando `EventId` (comportamiento heredado de F3-02, documentado en la firma de
  `KafkaEventPublisher` como "sin ninguna garantía de orden entre eventos relacionados").
- **`AggregateId` como `PartitionKey`** — orden por entidad de negocio: cuando eventos del mismo
  agregado deben procesarse en el orden exacto en que ocurrieron (por ejemplo, `PedidoCreado` antes que
  `PedidoCancelado` del mismo pedido), sin que importe el orden relativo frente a eventos de OTROS
  agregados. Se usa el `Id` (como `string`) del `AggregateRoot<TId>` (Shared.Kernel, F1-23) que levantó
  el `DomainEvent` original.

  ```csharp
  public sealed record PedidoCreadoIntegrationEvent(Guid PedidoId, string Cliente)
      : IntegrationEvent, IHasPartitionKey
  {
      public override string EventType => "Pedidos.PedidoCreado";
      public string PartitionKey => PedidoId.ToString();
  }
  ```

- **`TenantId` como `PartitionKey`** — orden por tenant: cuando no importa el orden relativo entre
  agregados distintos, pero sí que ningún evento de un tenant se procese fuera de orden respecto de otro
  evento del MISMO tenant (por ejemplo, un proyector de reportes por tenant). Se usa
  `ITenantEntity.TenantId` del origen del evento.

  ```csharp
  public sealed record PedidoCreadoIntegrationEvent(Guid PedidoId, Guid TenantId, string Cliente)
      : IntegrationEvent, IHasPartitionKey
  {
      public override string EventType => "Pedidos.PedidoCreado";
      public string PartitionKey => TenantId.ToString();
  }
  ```

- **Semántica de orden resultante:** Kafka enruta por hash de `Key` — misma `Key` → misma partición del
  tópico → un consumidor de esa partición recibe los mensajes en el mismo orden en que se publicaron
  (orden garantizado). Dos eventos con `PartitionKey` distinta PUEDEN terminar en la misma partición o
  en particiones distintas (no hay ninguna garantía de orden relativo entre ellos en ningún caso) —
  exactamente la semántica exigida por la Fase 3: "Orden: solo garantizado dentro de la partición
  definida".
- **Elección del criterio (`AggregateId` vs `TenantId`) es del autor del evento concreto**, no una regla
  única y universal que este framework pueda imponer: F3-05 provee el mecanismo (`IHasPartitionKey`),
  no la política de qué campo usar para cada evento de negocio.
- **No resuelto por F3-05:** un esquema de tópicos por tenant/ambiente (sigue siendo
  `IKafkaTopicNameResolver`, F3-02), la cantidad de particiones de un tópico productivo ni ninguna
  política de reparticionamiento — quedan fuera del alcance de esta tarea.

Pruebas: `tests/Shared.Infrastructure.Messaging.Kafka.Tests/Integration/KafkaEventPublisherPartitioningIntegrationTests.cs`
verifica, contra un tópico Kafka real de 3 particiones (`Confluent.Kafka.Admin.AdminClient`, criterio de
aceptación literal "Orden demostrado por partición"): (1) N eventos con la misma `PartitionKey` llegan a
un consumidor en el mismo orden exacto en que se publicaron y todos caen en la misma partición; (2)
eventos con `PartitionKey` distinta pueden repartirse en más de una partición (sin garantía de orden
relativo entre ellos); (3) un evento que NO implementa `IHasPartitionKey` sigue publicándose con `Key` =
`EventId`, sin romper la compatibilidad con F3-01 a F3-04. `tests/Shared.Application.Tests/Eventing/EventingContractsTests.cs`
cubre la forma del contrato (`IHasPartitionKey` opcional, ejemplo `AggregateId` y ejemplo `TenantId`) sin
necesitar ningún broker.

## Compatibilidad de esquema (F3-06)

F3-06 cierra lo que F3-01 dejó reservado en `IIntegrationEvent.SchemaVersion` ("la política de
compatibilidad forward/backward concreta sobre este campo es trabajo de F3-06") con dos entregables:
las reglas concretas (ya integradas en `docs/politica-versionado.md`, sección 5) y un mecanismo de
validación **ejecutable** que corre en CI sin necesitar ningún broker.

### Reglas de compatibilidad forward/backward (resumen operativo)

Entre la versión N (`SchemaVersion = N`) y N+1 de un mismo `IIntegrationEvent` (mismo `EventType`):

| Cambio en el payload | ¿Rompe? | Acción requerida |
|---|---|---|
| Agregar un campo nuevo **nullable** (`T?`) | No — aditivo | Ninguna, `SchemaVersion` no cambia |
| Eliminar un campo existente | Sí | Incrementar `SchemaVersion`, ventana de coexistencia |
| Cambiar el tipo CLR de un campo existente | Sí | Incrementar `SchemaVersion`, ventana de coexistencia |
| Agregar un campo nuevo **no nullable** (requerido) | Sí | Incrementar `SchemaVersion`, ventana de coexistencia |
| Cambiar el significado semántico de un campo sin cambiar su forma | Sí (no detectable automáticamente) | Incrementar `SchemaVersion`; requiere revisión humana del cambio, el checker no lo detecta |

La razón de usar "nullable" como criterio de "opcional" (en vez de, por ejemplo, un atributo custom o
un valor default de constructor) es que es la señal que `System.Text.Json` ya usa de forma coherente
con la deserialización tolerante (miembros desconocidos ignorados por defecto,
`KafkaIntegrationEventSerializer`, F3-02): un campo nullable ausente en un payload viejo deserializa a
`null`/valor por defecto sin lanzar, y un consumidor viejo frente a un payload nuevo con un campo
nullable adicional simplemente lo ignora.

### Mecanismo de validación ejecutable

`EventSchemaCompatibilityChecker` (`BitCode.Framework.Shared.Testing`, `src/Shared.Testing/EventSchemaCompatibilityChecker.cs`)
compara por reflexión dos tipos .NET concretos de un mismo evento:

```csharp
public static EventSchemaCompatibilityResult CheckBackwardCompatibility(Type previousVersion, Type currentVersion)
```

`EventSchemaCompatibilityResult.IsBackwardCompatible` es `false` si encuentra alguna de las tres
primeras violaciones de la tabla de arriba; `Violations` trae el detalle en texto legible (nombre del
campo, versión de origen/destino y la regla de `docs/politica-versionado.md` que se violó), listo para
un mensaje de aserción de test. Los cuatro campos del envelope de `IIntegrationEvent` (`EventId`,
`OccurredOnUtc`, `EventType`, `SchemaVersion`) quedan excluidos de la comparación — no son parte del
payload de datos específico del evento.

**Por qué en `Shared.Testing` y no en `Shared.Application`:** es una herramienta de prueba (reflexión
sobre tipos, pensada para usarse solo desde proyectos de test), no un componente que participe en
tiempo de ejecución de publicación/consumo de eventos — mismo criterio que ya aplican
`KafkaContainerFixture`/`SqlServerContainerFixture` en el mismo proyecto. `Shared.Testing` ya está
sujeto al mismo gate de compatibilidad de API pública que el resto de `src/`
(`docs/gate-compatibilidad-api.md`), así que un cambio futuro a la forma de
`EventSchemaCompatibilityChecker`/`EventSchemaCompatibilityResult` pasa por el mismo análisis de
impacto que cualquier otro contrato público del framework.

**Por qué reflexión sobre tipos .NET y no un snapshot de JSON Schema committeado:** el repositorio no
tiene todavía ningún evento de negocio real (solo los de ejemplo en
`tests/Shared.Application.Tests/Eventing/TestIntegrationEvents.cs`) ni una librería de JSON Schema ya
evaluada (`docs/politica-dependencias.md`); comparar directamente los tipos .NET concretos da la misma
señal (forma del payload) sin agregar una dependencia nueva ni un artefacto de snapshot adicional que
mantener sincronizado a mano. Si en el futuro el catálogo de eventos (F3-12) necesita publicar JSON
Schema real para consumidores no-.NET, ese es un mecanismo complementario a evaluar en esa tarea, no un
reemplazo de este checker interno.

**Prueba de referencia (criterio de aceptación "Contratos validados en CI" y "Schema compatible e
incompatible" de la Fase 3):** `tests/Shared.Application.Tests/Eventing/EventSchemaCompatibilityTests.cs`
corre en el job `test-unit` de `.github/workflows/ci.yml` (no tiene `Integration` en el nombre, no
requiere Testcontainers) y cubre:

- **Caso compatible:** `TestOrderCreatedIntegrationEvent` (V1) →
  `TestOrderCreatedWithOptionalDiscountIntegrationEvent` (agrega `DiscountAmount` nullable, mismo
  `SchemaVersion = 1`) — `IsBackwardCompatible` es `true`.
- **Caso incompatible (campo eliminado):** V1 →
  `TestOrderCreatedMissingCustomerNameIntegrationEvent` (elimina `CustomerName`) —
  `IsBackwardCompatible` es `false`.
- **Caso incompatible (tipo cambiado):** V1 →
  `TestOrderCreatedWithCustomerNameAsNumberIntegrationEvent` (`CustomerName` pasa de `string` a `int`)
  — `IsBackwardCompatible` es `false`.
- **Caso incompatible (campo requerido agregado), evento real de F3-01:** V1 →
  `TestOrderCreatedV2IntegrationEvent` (agrega `Total`, `decimal` no nullable) —
  `IsBackwardCompatible` es `false`; este caso documenta explícitamente POR QUÉ
  `TestOrderCreatedV2IntegrationEvent` (creado en F3-01 como ejemplo de "evolución de esquema") tuvo
  que incrementar `SchemaVersion` a `2` en vez de mantenerse en `1`.

Cualquier bounded context con eventos de negocio reales puede replicar este mismo patrón: cuando
declara `PedidoCreadoV2IntegrationEvent`, un test que llama a
`EventSchemaCompatibilityChecker.CheckBackwardCompatibility(typeof(PedidoCreadoIntegrationEvent), typeof(PedidoCreadoV2IntegrationEvent))`
documenta y verifica en CI si la evolución fue realmente aditiva o si ameritaba (correctamente) el
incremento de `SchemaVersion`.

### Qué NO resuelve F3-06

- No genera ni publica JSON Schema/Avro/Protobuf real para consumidores no-.NET — sigue siendo JSON
  libre serializado del tipo .NET concreto (decisión ya tomada en F3-02).
- No detecta cambios de significado semántico de un campo que mantiene su forma (mismo nombre, mismo
  tipo, distinto significado) — la tabla de arriba lo marca explícitamente como "no detectable
  automáticamente"; sigue dependiendo de la revisión humana del cambio, igual que cualquier otro
  contrato público (`docs/politica-versionado.md`, sección 1).
- No fuerza ni automatiza el incremento real de `SchemaVersion` en el evento — es responsabilidad del
  autor del cambio; el checker solo confirma si la forma resultante habría sido compatible sin ese
  incremento.
- No valida compatibilidad contra ningún evento ya publicado en un tópico Kafka real (no hay registro
  de esquema/Schema Registry, F3-02) — la comparación es siempre entre dos tipos .NET del propio
  repositorio, en tiempo de compilación/test.
- Catálogo de eventos con owner/PII/consumidores registrados (F3-12), DLQ (F3-08),
  poison messages (F3-09), observabilidad (F3-10) ni seguridad de transporte (F3-11).

## Reintentos con backoff y límite (F3-07)

Ver `docs/politica-reintentos-eventos.md` para el detalle completo. Resumen: `OutboxBatchProcessor`
(relay de Outbox, F3-03) y `KafkaEventConsumer<TEvent>` (F3-02/F3-04) ahora clasifican cada fallo con
`IEventPublishFailureClassifier` (`Shared.Application.Eventing`; `KafkaEventPublishFailureClassifier`
es la implementación real contra `Confluent.Kafka.ProduceException`) y aplican backoff exponencial con
jitter (`EventRetryBackoff`) hasta `EventRetryPolicyOptions.MaxAttempts` — al superarlo (o ante un error
permanente), el relay de Outbox marca `OutboxMessage.ExhaustedAtUtc` (la fila nunca se pierde ni se
descarta, solo deja de reclamarse) y el consumidor Kafka lanza `EventProcessingExhaustedException` (el
offset sigue sin confirmarse). Ninguno de los dos implementa DLQ real todavía (F3-08).

### Qué NO resuelve F3-07

- DLQ real (F3-08), aislamiento de poison messages más allá del límite de reintentos (F3-09),
  observabilidad/métricas dedicadas de reintentos (F3-10).
- Persistencia del conteo de intentos fallidos del lado consumidor (queda en memoria, no persistido —
  ver la tabla comparativa en `docs/politica-reintentos-eventos.md`).

## Referencias

- `src/Shared.Application/Eventing/IIntegrationEvent.cs`, `IntegrationEvent.cs`, `IEventPublisher.cs`, `IEventConsumer.cs`, `IHasPartitionKey.cs` (F3-05).
- `tests/Shared.Application.Tests/Eventing/EventingContractsTests.cs`, `TestIntegrationEvents.cs`, `EventSchemaCompatibilityTests.cs` (F3-06).
- `src/Shared.Testing/EventSchemaCompatibilityChecker.cs` (F3-06), `src/Shared.Testing/PublicAPI.Unshipped.txt`.
- `src/Shared.Infrastructure.Messaging.Kafka/` (F3-02/F3-05): `KafkaMessagingOptions.cs`, `KafkaClientConfigFactory.cs`, `IKafkaTopicNameResolver.cs`, `KafkaIntegrationEventSerializer.cs`, `KafkaEventPublisher.cs`, `KafkaEventConsumer.cs`, `KafkaServiceCollectionExtensions.cs`.
- `src/Shared.Infrastructure.Persistence/Outbox/` (F3-03): `OutboxBatchProcessor.cs`, `OutboxBatchResult.cs`, `OutboxPublisherOptions.cs`, `OutboxPublisherBackgroundService.cs`, `OutboxPublisherServiceCollectionExtensions.cs`. Ver `docs/guia-outbox-publisher.md` para el detalle completo.
- `src/Shared.Testing/KafkaContainerFixture.cs` y `tests/Shared.Infrastructure.Messaging.Kafka.Tests/`, incluida `Integration/KafkaEventPublisherPartitioningIntegrationTests.cs` (F3-05).
- `tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxPublisherIntegrationTests.cs` (F3-03).
- `docs/guia-inbox-consumer.md` (F3-04, coordinación Kafka + Inbox), `tests/Shared.Infrastructure.Persistence.Tests/Integration/InboxConsumerIntegrationTests.cs`.
- `docs/convenciones.md` (regla dura 17/18/22/23/25, Outbox/Inbox/adapter Kafka/relay de Outbox/particionamiento/reintentos, F1-23/F1-24/F3-02/F3-03/F3-05/F3-07).
- `docs/politica-reintentos-eventos.md` (F3-07, política completa de reintentos).
- `docs/matriz-soporte.md` (imagen de Kafka usada en test, brechas de SASL/SSL/Inbox).
- `docs/politica-dependencias.md` (evaluación de `Confluent.Kafka`/`Testcontainers.Kafka`).
- `docs/politica-versionado.md`, sección 5 (reglas de compatibilidad forward/backward de eventos de integración, actualizada por F3-06).
- `docs/gate-compatibilidad-api.md` (gate de superficie pública que también cubre `Shared.Testing`, incluido `EventSchemaCompatibilityChecker`).
- ADR `docs/adr/0005-mensajeria-kafka.md` (`Accepted` desde F3-02).
