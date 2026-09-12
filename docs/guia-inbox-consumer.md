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

## Reintentos clasificados y backoff (F3-07)

`KafkaEventConsumer<TEvent>.ConsumeAndHandleOnceAsync` ahora clasifica cualquier excepción que
`IInboxMessageProcessor.ProcessAsync` deje pasar (`IEventPublishFailureClassifier`,
`Shared.Application.Eventing`) y, mientras quede margen (`EventRetryPolicyOptions.MaxAttempts`, expuesto
como `KafkaEventConsumer<TEvent>.RetryOptions`), espera un backoff exponencial con jitter
(`EventRetryBackoff.CalculateDelay`) ANTES de volver a lanzar la excepción original — así el offset sigue
sin confirmarse (Kafka reentrega el mismo mensaje) pero sin que el host llamador reintente en un loop
apretado sin ninguna pausa. Al agotar el margen (o si el error se clasifica como
`EventPublishFailureKind.Permanent`), lanza `EventProcessingExhaustedException` en vez de la excepción
original — el punto de extensión explícito que F3-08 (DLQ) necesita.

Ver `docs/politica-reintentos-eventos.md` para el detalle completo. Dos limitaciones reales de diseño de
este lado (no compartidas por el relay de Outbox, que persiste su estado en `OutboxMessage`):

- **El backoff es bloqueante.** A diferencia del relay de Outbox (que solo programa una marca de tiempo
  futura sin bloquear nada, consultada recién en el siguiente ciclo de sondeo),
  `ConsumeAndHandleOnceAsync` no tiene ningún lugar donde "programar" un reintento futuro sin bloquear:
  no hay loop propio (lo maneja el host que la invoca) y Kafka reentrega el mismo mensaje en la
  siguiente llamada sin que este consumidor pueda decirle "esperá". La única forma de introducir backoff
  real es demorar la propia llamada que falló con `Task.Delay` antes de devolver el control — lo que
  retrasa también el procesamiento de cualquier mensaje siguiente que este consumidor reciba después.
- **El conteo de intentos es EN MEMORIA, no persistido.** Un reinicio del proceso pierde el conteo y el
  mensaje vuelve a tener margen completo de reintentos — a diferencia de `OutboxMessage.RetryCount`
  (persistido en SQL Server), Inbox (F1-24) solo persiste mensajes que terminaron con éxito, nunca
  intentos fallidos; agregar esa persistencia es un cambio de esquema de Inbox fuera del alcance mínimo
  de F3-07. El límite máximo de reintentos del lado consumidor es, por lo tanto, una protección "mejor
  esfuerzo" dentro de la vida de un mismo proceso, no una garantía dura de "nunca más de N intentos
  totales" como sí lo es del lado del relay de Outbox.

## Poison messages (F3-09) vs. agotamiento de reintentos del handler (F3-07)

Son dos fallos distintos, con dos tratamientos distintos dentro de `ConsumeAndHandleOnceAsync`:

| | Poison message (F3-09) | Agotamiento de reintentos (F3-07) |
|---|---|---|
| ¿Dónde falla? | `KafkaIntegrationEventSerializer.Deserialize<TEvent>` — ANTES de abrir el scope de DI / invocar Inbox. | `IEventConsumer<TEvent>.ConsumeAsync` (el handler de negocio), dentro de `IInboxMessageProcessor.ProcessAsync`. |
| ¿El mensaje se pudo interpretar? | No — nunca llegó a existir una instancia de `TEvent`. | Sí — el evento se deserializó e interpretó correctamente. |
| ¿Es transitorio o permanente? | Siempre permanente: un JSON corrupto o de forma incompatible no se arregla reintentando. | Depende (`IEventPublishFailureClassifier`): puede ser transitorio (dependencia caída) o permanente. |
| ¿Pasa por backoff/reintentos? | No, nunca — ni siquiera un intento adicional. | Sí, hasta `EventRetryPolicyOptions.MaxAttempts` (o hasta clasificarse `Permanent`). |
| ¿Incrementa `_attemptsByMessageId`? | No. | Sí. |
| ¿A dónde va? | Directo a DLQ (`IsolatePoisonMessageAsync`), `DeadLetterEnvelope.Reason` empieza con `"PoisonMessage"`. | A DLQ solo al agotar el margen (`PublishToDeadLetterAsync`), `Reason` es el mensaje de la excepción de negocio. |
| ¿Se confirma el offset? | Sí, siempre (incluso si la publicación a DLQ también falla). | Solo al agotar el margen (mientras queda margen, el offset NO se confirma y Kafka reentrega). |

Ambos casos terminan aislados en el mismo tópico dead-letter (mismo `IDeadLetterPublisher`) con el offset
confirmado — la diferencia observable para un operador es el motivo (`Reason`) del registro DLQ, no el
tópico. Ver el `remarks` de la clase `KafkaEventConsumer<TEvent>` para el detalle completo.

## Límites conocidos

- **DLQ y aislamiento de poison messages best-effort (F3-08/F3-09).** Si `IDeadLetterPublisher` no está
  configurado, o la publicación a DLQ también falla, el offset se confirma igual (solo queda un warning en
  el log) — la alternativa (bloquear la partición hasta que el DLQ vuelva) dejaría mensajes siguientes
  válidos sin procesar por un mensaje que, en el caso de un poison message, NUNCA se va a poder
  deserializar aunque el DLQ vuelva a estar disponible. El costo aceptado es perder esa copia dead-letter
  puntual.
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

## Boundary de contratos (F9-02)

Fase 9 (`docs/plan-maestro-bitcode-ia.md`, backlog F9-02, "Contract boundary") eligió Workflow como
módulo piloto de extracción como microservicio
(`docs/adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md`). Ese ADR encontró, como condición
explícita antes de cualquier otro trabajo de Fase 9, que sus tres consumidores reales de eventos
(Task Inbox, Notifications y, se confirmó al ejecutar F9-02, también Reporting) tenían un
`ProjectReference` DIRECTO contra el ensamblado COMPLETO de `BitCode.Platform.Workflow` (motor de
estados, `WorkflowDbContext`, comandos/queries internos) solo para poder usar el tipo .NET de sus
eventos de integración — el acoplamiento de compilación que impediría extraer Workflow sin romper a
sus consumidores.

**Solución aplicada:** los 6 eventos de integración públicos de Workflow
(`TareaAsignada/Aprobada/RechazadaIntegrationEvent`, `WorkflowInstanciaIniciada/
FinalizadaIntegrationEvent`, `WorkflowVersionPublicadaIntegrationEvent`) se movieron a un ensamblado
nuevo de SOLO contratos, `BitCode.Platform.Workflow.Contracts`
(`src/Platform/BitCode.Platform.Workflow.Contracts`) — sin `WorkflowDbContext`, sin handlers, sin
lógica de negocio, sin ninguna otra dependencia además de `Shared.Kernel` (por `DomainEvent`) y
`Shared.Application` (por `IIntegrationEvent`/`IHasPartitionKey`). Tanto `BitCode.Platform.Workflow`
(que sigue siendo quien LEVANTA estos eventos sobre sus propios agregados, `WorkflowInstance`/
`WorkflowTask`/`WorkflowVersion`) como sus tres consumidores ahora referencian ese ensamblado de
contratos; Task Inbox, Notifications y Reporting **ya no tienen** `ProjectReference` al proyecto
completo de Workflow.

Los tipos conservan DELIBERADAMENTE el mismo namespace que tenían dentro de
`BitCode.Platform.Workflow` (`BitCode.Framework.Platform.Workflow.Instancias`/`.Definiciones`): esto
significa que ningún archivo consumidor existente (los `IEventConsumer<TEvent>` de Task Inbox/
Notifications/Reporting) necesitó cambiar un solo `using` — el único cambio real fue el
`ProjectReference` en cada `.csproj`. Se verificó con los cuatro proyectos de test de integración
existentes contra SQL Server real (Testcontainers) que el fan-out de eventos sigue funcionando
exactamente igual después del cambio: `samples/Sample.Workflow.Api.Tests`,
`samples/Sample.TaskInbox.Api.Tests`, `samples/Sample.Notifications.Api.Tests`,
`samples/Sample.Reporting.Api.Tests`.

**Límite honesto que sí queda (no resuelto por esta tarea):** `OutboxBatchProcessor`
(`src/Shared.Infrastructure.Persistence/Outbox/OutboxBatchProcessor.cs`) resuelve el tipo de cada
`OutboxMessage` pendiente con `Type.GetType(message.EventType, ...)`, donde `EventType` guarda el
`Type.AssemblyQualifiedName` del `DomainEvent` en el momento en que se persistió la fila — ese nombre
incluye el ensamblado. Al mover los 6 eventos de Workflow a `BitCode.Platform.Workflow.Contracts`, el
`AssemblyQualifiedName` que Workflow escribe en filas NUEVAS de Outbox apunta ahora a ese ensamblado
nuevo. Cualquier fila de `OutboxMessage` que ya hubiera quedado PENDIENTE de publicar (no procesada)
en el momento exacto de desplegar este cambio, con el `AssemblyQualifiedName` de ANTES (apuntando al
ensamblado `BitCode.Platform.Workflow`), quedaría con un tipo irresolvible y se marcaría como fallo
permanente (mismo camino que cualquier tipo borrado, ver remarks de `OutboxBatchProcessor`). Se evaluó
mitigar esto con `[assembly: TypeForwardedTo(...)]` en `BitCode.Platform.Workflow` (mecanismo estándar
de .NET para este escenario), pero se descartó para esta tarea: el analizador de compatibilidad de API
pública (`docs/gate-compatibilidad-api.md`, F1-03) trata cada tipo reenviado como superficie pública
del ensamblado de origen otra vez, duplicando el mantenimiento de `PublicAPI.*.txt` entre dos proyectos
por cada evento — costo que no se justifica hoy porque, como documenta explícitamente
`docs/adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md`, este framework no tiene tráfico
productivo real ni un release publicado todavía. **Pendiente explícito para cuando exista tráfico
productivo real** (F9-05 en adelante, o antes si se habilita producción): o bien reintroducir
`TypeForwardedTo` aceptando el costo de mantenimiento de `PublicAPI.*.txt`, o bien operar el despliegue
de este tipo de cambio drenando el Outbox de Workflow (esperar a que `OutboxPublisherBackgroundService`
procese todas las filas pendientes) antes de desplegar la nueva versión del binario.

**Otra limitación honesta:** este ensamblado de contratos vive hoy como un proyecto más de la misma
solución/monorepo (`BitCode.Framework.slnx`), empaquetado igual que el resto (ver
`docs/politica-empaquetado.md`) pero NO publicado todavía como paquete NuGet versionado de forma
independiente. Si en el futuro Workflow se extrae de verdad como servicio en un repositorio/proceso de
despliegue separado (Fase 9, F9-05 "Host independiente" en adelante), sus consumidores necesitarán
consumir `BitCode.Platform.Workflow.Contracts` como una dependencia externa versionada (paquete NuGet
publicado, con su propia política de compatibilidad), no como `ProjectReference` dentro del mismo
repo — ese es trabajo explícitamente fuera del alcance de F9-02.

## Referencias

- `src/Platform/BitCode.Platform.Workflow.Contracts` (F9-02, ensamblado de solo contratos de los 6
  eventos de integración públicos de Workflow).
- `docs/adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md` (Fase 9, selección de Workflow
  como módulo piloto y condición de F9-02).
- `src/Shared.Infrastructure.Messaging.Kafka/KafkaEventConsumer.cs` (F3-04: coordinación con Inbox; F3-07: reintentos clasificados y backoff; F3-09: aislamiento de mensajes poison).
- `docs/runbook-dlq.md` (F3-08/F3-09, operación de la DLQ, incluida la distinción poison vs. agotamiento de reintentos).
- `src/Shared.Infrastructure.Messaging.Kafka/KafkaEventPublishFailureClassifier.cs` (F3-07).
- `src/Shared.Application/Eventing/EventRetryPolicyOptions.cs`, `EventRetryBackoff.cs`, `EventProcessingExhaustedException.cs` (F3-07).
- `src/Shared.Application/Inbox/IInboxMessageProcessor.cs`, `InboxMessageProcessor.cs` (F1-24).
- `src/Shared.Domain/Inbox/IInboxStore.cs`, `InboxMessage.cs` (F1-24).
- `tests/Shared.Infrastructure.Persistence.Tests/Integration/InboxConsumerIntegrationTests.cs`,
  `InboxConsumerCollection.cs` (F3-04).
- `tests/Shared.Infrastructure.Messaging.Kafka.Tests/InMemoryInboxMessageProcessor.cs` (F3-04, test
  double para pruebas de F3-02 sin SQL Server real).
- `docs/politica-reintentos-eventos.md` (F3-07, política completa).
- `docs/guia-eventing-contratos.md` (contratos F3-01, adapter Kafka F3-02, relay de Outbox F3-03).
- `docs/guia-outbox-publisher.md` (F3-03, patrón de referencia de "scope por unidad de trabajo"; F3-07, reintentos del lado publicador).
- `docs/convenciones.md` (regla dura 17/18/22/23, Outbox/Inbox/adapter Kafka/relay de Outbox).
- ADR `docs/adr/0005-mensajeria-kafka.md` (`Accepted` desde F3-02).
