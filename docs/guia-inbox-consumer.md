# Inbox Consumer (F3-04)

## Objetivo

F3-02 (`KafkaEventConsumer<TEvent>`) resolvía, en aislamiento, "puedo suscribirme a un tópico Kafka
real, deserializar el mensaje y pasárselo a un handler de negocio" — deliberadamente sin coordinar con
Inbox (F1-24). F3-04 cierra ese vacío: `KafkaEventConsumer<TEvent>.ConsumeAndHandleOnceAsync` ahora pasa
cada mensaje por `IInboxMessageProcessor.ProcessAsync` (F1-24) ANTES de confirmar el offset ante Kafka,
para que un mensaje reentregado (rebalance, reinicio del consumidor, o el duplicado aceptable que puede
introducir el relay de Outbox, F3-03) nunca vuelva a ejecutar el efecto de negocio.

Criterio de aceptación del Plan Maestro: **"duplicados no repiten efectos"**.

## Composición completa por mensaje

```
Kafka entrega el mensaje
  → se deserializa a TEvent (KafkaIntegrationEventSerializer.Deserialize<TEvent>)
  → se abre un scope de DI nuevo (IServiceScopeFactory.CreateAsyncScope)
  → se resuelven, DE ESE MISMO scope: IInboxMessageProcessor e IEventConsumer<TEvent>
  → se invoca IInboxMessageProcessor.ProcessAsync(EventId, EventType, payload, handler)
      donde handler = ct => IEventConsumer<TEvent>.ConsumeAsync(evento, ct)
  → si ProcessAsync retorna sin lanzar (Processed o Discarded) → se confirma el offset (Commit)
  → si ProcessAsync lanza (el handler de negocio falló) → el offset NO se confirma; el mismo mensaje
    se reintenta en la próxima llamada, sin ninguna fila de Inbox sobreviviendo (ver remarks de
    InboxMessageProcessor, F1-24)
```

`IIntegrationEvent.EventId` es el `messageId` que identifica el mensaje ante el Inbox: es la clave
natural de deduplicación documentada por el propio contrato (F3-01), estable entre reentregas del mismo
mensaje — nunca el offset/partición de Kafka, que identifica la posición física en el tópico, no la
instancia lógica del evento.

## Por qué un scope de DI por mensaje

Tanto `IInboxMessageProcessor` como cualquier `IEventConsumer<TEvent>` real necesitan el mismo
`DbContext`/`IUnitOfWork` de scope para que la fila de Inbox y el efecto de negocio se persistan en el
mismo `SaveChangesAsync` atómico (ver remarks de `InboxMessageProcessor`, F1-24). Un `DbContext` es
`Scoped` por diseño de EF Core: nunca puede compartirse de forma segura entre mensajes consumidos
secuencialmente por la misma instancia de `KafkaEventConsumer<TEvent>`, ni mucho menos entre llamadas
concurrentes. Es el mismo motivo por el que `OutboxPublisherBackgroundService` (F3-03) crea un scope de
DI nuevo por ciclo en vez de inyectar `OutboxBatchProcessor` directamente en su constructor — la
contraparte, del lado consumidor, de "scope por unidad de trabajo".

Antes de F3-04, `KafkaEventConsumer<TEvent>` recibía un `IEventConsumer<TEvent> handler` ya construido en
su constructor (una única instancia para toda la vida del consumidor). F3-04 cambia esa firma: el
constructor ahora recibe un `IServiceScopeFactory scopeFactory`, y crea un scope nuevo por mensaje del que
resuelve tanto `IInboxMessageProcessor` como `IEventConsumer<TEvent>`. Este es un cambio de contrato
público deliberado (justificado por el propio remarks de F3-02, que ya documentaba esta limitación
pendiente) — `KafkaEventConsumer<TEvent>` seguía en `PublicAPI.Unshipped.txt` (nunca shipeada una versión
con la firma anterior), así que no rompe compatibilidad de un paquete ya publicado.

### Cómo registrar el propio `IEventConsumer<TEvent>`

El bounded context que usa este consumidor debe registrar su propio `IEventConsumer<TEvent>` concreto
como `Scoped` en el mismo contenedor de DI raíz del que sale el `IServiceScopeFactory` que se le pasa al
constructor de `KafkaEventConsumer<TEvent>` (típicamente el `IServiceProvider` de la aplicación completa):

```csharp
services.AddSharedPersistence<MiDbContext>(connectionString);
services.AddSharedApplication(typeof(Program).Assembly); // registra IInboxMessageProcessor (Scoped)
services.AddScoped<IEventConsumer<PedidoCreadoIntegrationEvent>, PedidoCreadoConsumer>();

// en el host que aloja el consumidor (por ejemplo, un BackgroundService propio del proyecto):
using var consumer = new KafkaEventConsumer<PedidoCreadoIntegrationEvent>(
    kafkaOptions,
    "Pedidos.PedidoCreado",
    serviceProvider.GetRequiredService<IServiceScopeFactory>());

while (!stoppingToken.IsCancellationRequested)
{
    await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(5), stoppingToken);
}
```

`Shared.Infrastructure.Messaging.Kafka` no impone ningún host concreto (a diferencia del relay de
Outbox, que sí trae su propio `OutboxPublisherBackgroundService`): cada evento de integración concreto
necesita su propio consumidor/tópico/grupo, por lo que la decisión de "cómo y cuándo correr el loop de
consumo" queda en el proyecto consumidor.

## Límites conocidos

- **Sin reintentos clasificados ni backoff (F3-07).** Si `ProcessAsync` lanza, la excepción se propaga tal
  cual a quien llamó `ConsumeAndHandleOnceAsync` — no hay clasificación de error transitorio/permanente
  ni backoff con jitter todavía. El proyecto consumidor decide cómo reaccionar (por ejemplo, un loop que
  loguea y continúa con el próximo ciclo de sondeo, igual que `OutboxPublisherBackgroundService` hace con
  un fallo de ciclo completo).
- **Sin DLQ ni aislamiento de poison messages (F3-08/F3-09).** Un mensaje cuyo handler falla
  indefinidamente se reintenta indefinidamente (Kafka nunca avanza el offset) — no hay todavía ninguna
  cola de mensajes muertos ni límite de reintentos.
- **Concurrencia entre particiones/consumidores del mismo `messageId`.** Documentado ya por
  `InboxMessageProcessor` (F1-24): dos entregas casi simultáneas del mismo mensaje (dos particiones,
  dos instancias del proceso) pueden ejecutar el handler dos veces en paralelo antes de que la primera
  alcance a persistir la fila de Inbox; el índice único (`TenantId`, `MessageId`) evita que ambas
  persistan, pero la segunda ejecución falla su `SaveChangesAsync` con una violación de índice único en
  vez de descartarse silenciosamente — el offset de esa segunda entrega no se confirma automáticamente
  (la excepción se propaga), así que Kafka la reintenta, y ese reintento sí encuentra la fila ya marcada
  como procesada y se descarta correctamente.
- **Particionamiento** (F3-05) fue cerrado por el lado publicador (`IHasPartitionKey`,
  `docs/guia-eventing-contratos.md`); este consumidor no cambia: sigue suscribiéndose al tópico completo
  (todas sus particiones) y no participa de la decisión de a qué partición fue cada mensaje.
  **Compatibilidad de esquema (F3-06), observabilidad/métricas dedicadas (F3-10)** siguen sin resolver,
  igual que en F3-02/F3-03.

## Pruebas

`tests/Shared.Infrastructure.Persistence.Tests/Integration/InboxConsumerIntegrationTests.cs` (SQL Server
real vía `SqlServerContainerFixture` + Kafka real vía `KafkaContainerFixture`, misma composición de
fixtures que `OutboxPublisherIntegrationTests`, F3-03):

- **Mensaje duplicado:** publicar el mismo `IIntegrationEvent` (mismo `EventId`) dos veces a un tópico
  real y consumirlo dos veces con `KafkaEventConsumer<TEvent>` ejecuta el efecto de negocio UNA sola vez;
  la fila `InboxMessage` para ese `EventId` queda única, con `ProcessedAtUtc` no nulo.
- **Consumer "reiniciado" antes de confirmar el offset:** simula que el proceso murió DESPUÉS de que
  `IInboxMessageProcessor.ProcessAsync` ya persistió la fila de Inbox como procesada (el handler ya corrió
  con éxito) pero ANTES de que `KafkaEventConsumer<TEvent>` alcanzara a confirmar el offset — sembrando
  directamente una fila `InboxMessage` ya marcada como procesada para el mismo `EventId` ANTES de invocar
  `ConsumeAndHandleOnceAsync`, reproduce exactamente lo que Kafka reentrega al reiniciar. El handler NO
  se ejecuta (Inbox lo descarta como duplicado) aunque el offset recién se confirme en ESE intento.

`tests/Shared.Infrastructure.Messaging.Kafka.Tests/Integration/KafkaEventPublisherConsumerIntegrationTests.cs`
(F3-02, actualizada para la nueva firma) sigue verificando el round-trip productor→consumidor contra
Kafka real, ahora usando `InMemoryInboxMessageProcessor` (test double sin SQL Server, ver
`tests/Shared.Infrastructure.Messaging.Kafka.Tests/InMemoryInboxMessageProcessor.cs`): ese proyecto
verifica el adapter Kafka en aislamiento, la coordinación real con Inbox contra SQL Server vive en
`InboxConsumerIntegrationTests`. `tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxPublisherIntegrationTests.cs`
(F3-03) usa un `PassthroughInboxMessageProcessor` equivalente por el mismo motivo (ese test verifica
duplicados aceptables del relay de Outbox contra el broker, no la deduplicación de Inbox en sí).

## Referencias

- `src/Shared.Infrastructure.Messaging.Kafka/KafkaEventConsumer.cs` (F3-04: coordinación con Inbox).
- `src/Shared.Application/Inbox/IInboxMessageProcessor.cs`, `InboxMessageProcessor.cs` (F1-24).
- `src/Shared.Domain/Inbox/IInboxStore.cs`, `InboxMessage.cs` (F1-24).
- `tests/Shared.Infrastructure.Persistence.Tests/Integration/InboxConsumerIntegrationTests.cs`,
  `InboxConsumerCollection.cs` (F3-04).
- `tests/Shared.Infrastructure.Messaging.Kafka.Tests/InMemoryInboxMessageProcessor.cs` (F3-04, test
  double para pruebas de F3-02 sin SQL Server real).
- `docs/guia-eventing-contratos.md` (contratos F3-01, adapter Kafka F3-02, relay de Outbox F3-03).
- `docs/guia-outbox-publisher.md` (F3-03, patrón de referencia de "scope por unidad de trabajo").
- `docs/convenciones.md` (regla dura 17/18/22/23, Outbox/Inbox/adapter Kafka/relay de Outbox).
- ADR `docs/adr/0005-mensajeria-kafka.md` (`Accepted` desde F3-02).
