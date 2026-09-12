# Relay de Outbox / Outbox Publisher (F3-03)

## Objetivo

F1-23 (Outbox base) ya garantiza que un `DomainEvent` levantado por un `AggregateRoot<TId>` queda
persistido, dentro de la MISMA transacción SQL que el cambio de negocio, como una fila `OutboxMessage`
con `ProcessedAtUtc` en `null`. Esa tarea deliberadamente no publicaba nada a ningún broker — dejaba
esa responsabilidad para "Fase 3". F3-03 cierra ese vacío: agrega el worker (`OutboxBatchProcessor` +
`OutboxPublisherBackgroundService`, `Shared.Infrastructure.Persistence.Outbox`) que lee por lotes esas
filas pendientes, reconstruye el `IIntegrationEvent` (F3-01) correspondiente a cada una y lo publica vía
`IEventPublisher` (F3-02, hoy Kafka) — marcando la fila como procesada SOLO después de que la
publicación tuvo éxito.

Criterio de aceptación del Plan Maestro: **"reinicio no pierde eventos"**.

## Por qué vive en `Shared.Infrastructure.Persistence`

`OutboxMessage` y `OutboxSaveChangesInterceptor` ya viven ahí (F1-23) — el worker que lee esa misma
tabla es la contraparte natural, no un componente separado. La alternativa considerada (un proyecto
nuevo `Shared.Infrastructure.Outbox`) se descartó: el worker necesita acceso directo al `DbContext`
registrado por `AddSharedPersistence<TContext>` (para el `FromSqlRaw` de bloqueo, ver más abajo) y a
los metadatos de EF Core del modelo (`context.Model.FindEntityType(typeof(OutboxMessage))`) — separar
esto en otro proyecto solo habría introducido una dependencia circular o una API pública adicional sin
ningún beneficio real de aislamiento. La única dependencia nueva que este proyecto adquiere es
`Shared.Application` (por `IEventPublisher`/`IIntegrationEvent`, F3-01) — no crea un ciclo:
`Shared.Application` solo referencia `Shared.Kernel`/`Shared.Domain`, nunca `Shared.Infrastructure.Persistence`.

## El mapeo `OutboxMessage` → `IIntegrationEvent` (la decisión más delicada)

`OutboxMessage.EventType` es el `Type.AssemblyQualifiedName` del `DomainEvent` **interno** — no
necesariamente el de un `IIntegrationEvent`. `DomainEvent` (Shared.Kernel) e `IIntegrationEvent`
(Shared.Application.Eventing) son contratos distintos y ninguno implementa al otro por defecto (ver
`docs/guia-eventing-contratos.md`, tabla comparativa). El relay necesita decidir, para cada fila, si
el evento original debe cruzar el límite del bounded context (publicarse) o si es un detalle interno
que nunca debió salir del proceso.

Se evaluaron dos alternativas:

1. **Tabla de mapeo/registro explícito por bounded context** (por ejemplo, un
   `IReadOnlyDictionary<Type, Func<DomainEvent, IIntegrationEvent>>` registrado en DI por cada módulo
   consumidor). Descartada: agrega una pieza de configuración adicional que hay que mantener
   sincronizada con cada nuevo `DomainEvent`, y el propio Plan Maestro (regla dura 17 de
   `docs/convenciones.md`) ya establece que la decisión de qué agregados participan del Outbox vive en
   el propio tipo (`AggregateRoot<TId>`/`IHasDomainEvents`), no en una tabla externa — este mapeo debía
   seguir el mismo principio por consistencia.
2. **El propio `record` del `DomainEvent` implementa también `IIntegrationEvent`** cuando su autor
   decide que ese hecho de negocio debe cruzar el límite del bounded context. **Elegida.**

### Cómo se usa en la práctica

```csharp
// DomainEvent puramente interno: nunca sale del proceso (comportamiento sin cambios desde F1-23).
public sealed record InventarioAjustadoEvent(Guid ProductoId, int Delta) : DomainEvent;

// DomainEvent que TAMBIÉN es un evento de integración: el relay lo publica a Kafka.
public sealed record PedidoCreadoEvent(Guid PedidoId, string Cliente) : DomainEvent, IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public string EventType => "Pedidos.PedidoCreado";
    public int SchemaVersion => 1;
}
```

`DomainEvent.OccurredOnUtc` (propiedad `get`-only fijada en el constructor) ya satisface el miembro
`OccurredOnUtc` que exige `IIntegrationEvent` — no hace falta declararla de nuevo. El agregado sigue
llamando `RaiseDomainEvent(new PedidoCreadoEvent(...))` exactamente igual que antes de F3-03: **no hay
ningún cambio en el lado emisor (F1-23)**, ni en `OutboxSaveChangesInterceptor`, ni en la forma de
levantar eventos desde un agregado.

### Cómo lo resuelve el relay

`OutboxBatchProcessor` (método privado, dentro de `ProcessBatchAsync`):

1. Resuelve el tipo con `Type.GetType(message.EventType, throwOnError: false)`.
2. Deserializa `message.PayloadJson` a ese tipo concreto (`JsonSerializer.Deserialize`).
3. Si el resultado implementa `IIntegrationEvent` → lo publica vía `IEventPublisher.PublishAsync` tal
   cual (sin ningún adapter/wrapper intermedio: la instancia deserializada YA es un `IIntegrationEvent`
   válido, con su propio `EventId`/`EventType`/`SchemaVersion` reales).
4. Si el resultado NO implementa `IIntegrationEvent` → es un `DomainEvent` puramente interno. La fila se
   marca como procesada de todos modos (ya fue "considerada" por el relay, no queda pendiente
   indefinidamente en la cola de trabajo) pero **nunca** se llama a `IEventPublisher` para ella.

### Por qué esta elección y no la otra

- **Cero configuración adicional.** Ningún registro DI, ningún archivo de mapeo, ningún atributo que
  mantener sincronizado. La decisión de "esto es un evento de integración" queda en el mismo lugar
  donde ya vive la decisión de "esto es un evento de dominio" — el propio `record`.
- **Coherente con el resto del framework.** Mismo principio que `AggregateRoot<TId>`/`IHasDomainEvents`
  (F1-23): el framework detecta comportamiento por la interfaz que implementa el tipo, no por una tabla
  de configuración externa (ver también la regla dura 7 de `docs/convenciones.md` para
  `IAuditedEntity`/`ISoftDelete`/`ITenantEntity`, el mismo patrón).
- **Sin runtime overhead de reflexión adicional.** El relay ya necesita deserializar el payload para
  poder hacer cualquier cosa con él (aun en la alternativa de la tabla de mapeo); comprobar
  `is IIntegrationEvent` sobre el resultado ya deserializado es gratis.
- **Limitación conocida y aceptada:** un bounded context que quiera exponer un evento de integración
  cuya forma difiera del `DomainEvent` interno (por ejemplo, para no acoplar el contrato público a un
  detalle de implementación del agregado) no puede usar este mecanismo directamente — tendría que
  levantar DOS eventos desde el agregado (uno interno, uno de integración) o hacer que el
  `DomainEvent`/`IIntegrationEvent` combinado exponga únicamente los campos que quiere hacer públicos.
  Esta tarea no resuelve ese caso de forma especial; queda como extensión natural para cuando un
  bounded context real lo necesite (no hay evidencia de esa necesidad hoy en el repo).

### Qué pasa si el tipo no se puede resolver/deserializar

Si `Type.GetType` devuelve `null` (por ejemplo, el ensamblado que declara el `DomainEvent` no está
cargado en el proceso que corre el relay) o la deserialización lanza, la fila **no** se marca como
procesada: se incrementa `OutboxMessage.RetryCount`, se registra el motivo en `OutboxMessage.Error` y se
libera el lock para que el próximo ciclo la reintente. Esto asume, como el resto del framework hoy es un
monolito modular, que el proceso que corre el relay es el MISMO proceso .NET donde el bounded context
que levantó el evento ya cargó su propio ensamblado — no hay todavía un mecanismo de reintento con
backoff clasificado (ese es F3-07) ni de aislamiento de "poison messages" (F3-09): un tipo
irresolvible de forma permanente queda reintentándose indefinidamente hasta que una tarea posterior
agregue esa clasificación.

## Bloqueo entre réplicas (evitar publicación duplicada en simultáneo)

El criterio de aceptación de la fila del backlog ("Leer por lotes, bloquear, publicar y marcar") exige
que dos instancias del worker (varias réplicas del proceso, ya anticipado por la Fase 4 de alta
disponibilidad) no publiquen el mismo mensaje al mismo tiempo. `OutboxBatchProcessor.ClaimBatchAsync`
resuelve esto con una única sentencia SQL (CTE + `UPDATE ... OUTPUT`) contra la tabla de
`OutboxMessage`:

```sql
WITH candidates AS (
    SELECT TOP (@batchSize) *
    FROM [OutboxMessage] WITH (UPDLOCK, ROWLOCK, READPAST)
    WHERE [ProcessedAtUtc] IS NULL
      AND ([LockedUntilUtc] IS NULL OR [LockedUntilUtc] < @now)
    ORDER BY [OccurredAtUtc] ASC, [Id] ASC
)
UPDATE candidates
SET [LockedUntilUtc] = @lockUntil, [LockedBy] = @workerId
OUTPUT inserted.*;
```

- `UPDLOCK, ROWLOCK, READPAST`: cada fila que otra instancia ya bloqueó con su propia sentencia (todavía
  en vuelo) se salta silenciosamente (`READPAST`) en vez de esperar a que se libere — dos instancias que
  sondean al mismo tiempo reclaman conjuntos disjuntos de filas sin bloquearse mutuamente.
- La sentencia es una única operación (auto-commit) — el lock de fila de SQL Server solo dura mientras
  esa sentencia corre. `OutboxMessage.LockedUntilUtc` (nuevo campo, F3-03) es la segunda mitad del
  mecanismo: mientras esté en el futuro, ninguna otra instancia puede reclamar la fila (aunque el lock
  de SQL Server ya se haya liberado), porque el `WHERE` de la próxima sentencia de claim la excluye.
  `OutboxMessage.LockedBy` (nuevo campo, F3-03) solo es diagnóstico — no participa de la lógica de
  bloqueo en sí, que es puramente por tiempo + `READPAST`.
- Si el proceso muere después de reclamar pero antes de terminar de procesar (o antes de liberar el
  lock explícitamente), la fila queda "huérfana" con `LockedUntilUtc` en el futuro por a lo sumo
  `OutboxPublisherOptions.LockDuration` (2 minutos por defecto) — pasado ese tiempo, cualquier instancia
  (incluida la misma, si se reinició) puede volver a reclamarla.
- Se descartó una alternativa con una columna `RowVersion`/concurrencia optimista (reintentar en un
  bucle ante conflicto): con `READPAST`, dos instancias concurrentes simplemente dividen el trabajo sin
  ningún conflicto que resolver, sin necesidad de reintentos a nivel de aplicación.

## Reinicio no pierde eventos (caídas antes/después del publish)

- **Caída antes de publicar:** la fila nunca salió de `ProcessedAtUtc = NULL`; el próximo ciclo (de esta
  instancia reiniciada o de otra) la reclama y publica con normalidad. Nada que perder.
- **Fallo de publicación (`IEventPublisher.PublishAsync` lanza):** la fila NO se marca como procesada;
  se incrementa `RetryCount`, se registra `Error` y (F3-07, ver sección dedicada más abajo) se clasifica
  el error y se programa el próximo intento con backoff exponencial y jitter (o se marca la fila como
  agotada, si corresponde) — ya no se libera el lock para reintento inmediato sin límite como hacía
  F3-03. Las demás filas del lote siguen procesándose con normalidad (mismo criterio documentado en la
  firma de `IEventPublisher.PublishAsync(IEnumerable<IIntegrationEvent>, ...)`: sin atomicidad entre
  eventos del lote frente al broker).
- **Caída DESPUÉS de publicar pero ANTES de marcar (el caso más delicado):** el `SaveChangesAsync` que
  marca `OutboxMessage.ProcessedAtUtc` se ejecuta INMEDIATAMENTE después de que la publicación de ESA
  fila individual tuvo éxito (no al final de todo el lote) — esto minimiza, sin eliminarlo del todo, el
  intervalo en el que el evento ya llegó al broker pero la fila todavía no quedó marcada. Si el proceso
  muere exactamente en ese intervalo, el reinicio (con el lock ya expirado o liberado) reclama la misma
  fila y la publica de nuevo: el evento llega dos veces al broker. Esto es un **duplicado aceptable**
  (semántica "at-least-once", Plan Maestro sección 3.2 — nunca se promete exactly-once de punta a
  punta), pero la fila nunca queda huérfana sin publicar y sin marcar a la vez.

## Registro (DI)

```csharp
services.AddSharedPersistence<MiDbContext>(connectionString);
services.AddSharedMessagingKafka(configuration); // o cualquier otro IEventPublisher concreto
services.AddSharedOutboxPublisher(options =>
{
    options.BatchSize = 50;                       // filas por ciclo de sondeo
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.LockDuration = TimeSpan.FromMinutes(2);
});
```

`AddSharedOutboxPublisher` registra `OutboxBatchProcessor` (scoped, reutiliza el `DbContext` de
`AddSharedPersistence`) y `OutboxPublisherBackgroundService` (`BackgroundService`, crea un scope de DI
nuevo por ciclo — el mismo motivo por el que cualquier `BackgroundService` que consume servicios
`Scoped` no puede inyectarlos directamente en su constructor). Requiere que un `IEventPublisher` ya
esté registrado (por ejemplo, `AddSharedMessagingKafka`, F3-02) — llamarlo antes de tener un publisher
concreto deja `OutboxBatchProcessor` sin poder resolver esa dependencia.

## Reintentos, backoff y límite máximo (F3-07)

Ver `docs/politica-reintentos-eventos.md` para el detalle completo de la política de reintentos
(clasificación transitorio/permanente, fórmula de backoff, límite máximo, y su equivalente del lado
consumidor en `KafkaEventConsumer<TEvent>`). Resumen aplicado a `OutboxBatchProcessor`:

1. Un fallo de `IEventPublisher.PublishAsync` se clasifica con `IEventPublishFailureClassifier`
   (`Shared.Application.Eventing`) — `KafkaEventPublishFailureClassifier` (registrado por
   `AddSharedMessagingKafka`) distingue un `ProduceException` transitorio (broker caído, timeout de red)
   de uno permanente (mensaje demasiado grande, no autorizado) usando `Error.IsFatal`/`Error.Code`.
2. **Transitorio, dentro de `OutboxPublisherOptions.Retry.MaxAttempts`:** se calcula un backoff
   exponencial con jitter (`EventRetryBackoff.CalculateDelay`, a partir de `OutboxMessage.RetryCount`) y
   se fija `OutboxMessage.LockedUntilUtc` a ese momento futuro — el mismo campo que ya protegía contra
   reclamos concurrentes entre réplicas (F3-03) ahora también funciona como "no reclamar antes de" para
   el backoff, sin necesidad de un campo nuevo.
3. **Permanente, o transitorio que agotó `MaxAttempts`:** se fija `OutboxMessage.ExhaustedAtUtc` — la
   fila deja de ser candidata en `ClaimBatchAsync` (nuevo filtro `ExhaustedAtUtc IS NULL`) para siempre,
   pero **nunca** se marca `ProcessedAtUtc` ni se borra: sigue existiendo, consultable, como punto de
   extensión explícito para F3-08 (DLQ) o intervención manual — perderla silenciosamente violaría
   "reinicio no pierde eventos" (F3-03).

`OutboxPublisherOptions.Retry` (tipo `EventRetryPolicyOptions`, `Shared.Application.Eventing`) expone
`MaxAttempts` (default 10), `BaseDelay` (default 2 s) y `MaxDelay` (default 5 min):

```csharp
services.AddSharedOutboxPublisher(options =>
{
    options.Retry.MaxAttempts = 8;
    options.Retry.BaseDelay = TimeSpan.FromSeconds(3);
    options.Retry.MaxDelay = TimeSpan.FromMinutes(10);
});
```

Un tipo irresolvible/payload no deserializable (la limitación que F3-03 dejaba documentada como
"pendiente de F3-07" en la sección anterior) ahora se clasifica siempre como error **permanente** —
ningún reintento futuro puede cambiar el hecho de que el tipo no existe o el JSON no coincide con él, así
que se agota en el primer intento en vez de reintentarse indefinidamente.

## Qué NO resuelve F3-07 (ni F3-03)

- **Inbox/consumo** (F3-04): este relay es exclusivamente el lado publicador (Outbox → broker).
- **Particionamiento definitivo**: cerrado por F3-05 (`IHasPartitionKey`, ver `docs/guia-eventing-contratos.md`) — un `DomainEvent`/`IIntegrationEvent` reconstruido por este relay que implementa `IHasPartitionKey` publica con `Key = PartitionKey`; si no la implementa, sigue siendo `Key = EventId` (heredado de F3-02). Este relay no toma ninguna decisión de partición por su cuenta: solo reconstruye el `IIntegrationEvent` y delega en `IEventPublisher.PublishAsync` (`KafkaEventPublisher`).
- **Compatibilidad de esquema** (F3-06, ya resuelta por su propia tarea).
- **DLQ real** (F3-08): F3-07 solo deja `OutboxMessage.ExhaustedAtUtc` como punto de extensión explícito
  (fila visible, nunca perdida, ya no reclamada) — no implementa ningún enrutamiento a un tópico de
  mensajes muertos ni ninguna herramienta de reprocesamiento/operador.
- **Aislamiento de poison messages más allá del límite de reintentos** (F3-09): un mensaje que agota
  `MaxAttempts` dentro de esta fila deja de bloquear el procesamiento de las demás filas del lote (ya
  ocurría desde F3-03), pero F3-07 no aísla nada adicional a nivel de tópico/partición.
- **Observabilidad/métricas dedicadas** (F3-10): el worker solo loguea vía `ILogger` (`LogWarning`/
  `LogError`, incluyendo ahora la clasificación y el resultado del reintento), no expone
  contadores/histogramas propios todavía.
- **Reconstrucción de un `IIntegrationEvent` con una forma distinta a la del `DomainEvent` interno**:
  ver la limitación conocida documentada arriba, en la sección del mapeo.

## Pruebas

`tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxPublisherIntegrationTests.cs`
(SQL Server real vía `SqlServerContainerFixture` + Kafka real vía `KafkaContainerFixture`, F3-02):

- Mensaje nunca intentado → se reclama y publica en el próximo ciclo ("caída antes de publicar").
- Fallo de publicación → la fila queda sin marcar, con `RetryCount` incrementado y (F3-07, con
  `BaseDelay` en cero para mantener el reintento inmediato en este test puntual)
  `LockedUntilUtc` programado; un ciclo posterior con un publisher que ya no falla la publica y marca
  con éxito.
- `DomainEvent` interno (no implementa `IIntegrationEvent`) → se marca como procesado sin llamar nunca
  a `IEventPublisher`.
- Caída después de publicar, antes de marcar (simulada publicando manualmente al broker real sin marcar
  la fila, y corriendo después un ciclo normal que la reclama de nuevo) → el broker recibe el evento dos
  veces (duplicado aceptable) pero la fila termina marcada como procesada.
- Dos instancias de `OutboxBatchProcessor` (cada una con su propio `DbContext`/scope) drenando el mismo
  backlog concurrentemente → el total publicado es exactamente igual al total sembrado, sin duplicados
  entre instancias (verifica el mecanismo de bloqueo).

`tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxPublisherRetryTests.cs` (F3-07, solo SQL
Server real — no necesita Kafka, el punto es la política de reintentos en sí):

- Error permanente (clasificador de prueba) → se agota en el PRIMER fallo, sin esperar a `MaxAttempts`;
  un ciclo posterior ya no vuelve a reclamar la fila ni a llamar al publisher.
- Error transitorio que nunca se recupera → se reintenta hasta `MaxAttempts` (inclusive) y recién ahí
  queda agotado; nunca se marca `ProcessedAtUtc` (no se pierde).
- Backoff real (`BaseDelay` distinto de cero) → un ciclo inmediatamente posterior a un fallo NO puede
  reclamar la fila todavía (el backoff no transcurrió).

`tests/Shared.Application.Tests/Eventing/EventRetryBackoffTests.cs` (F3-07, cálculo puro, sin SQL Server
ni Kafka): crecimiento exponencial del tope entre intentos sucesivos, tope superior nunca excede
`MaxDelay`, el jitter siempre cae en `[0, tope]`, y `IsExhausted` compara correctamente contra
`MaxAttempts`.

## Referencias

- `src/Shared.Domain/Outbox/OutboxMessage.cs` (`LockedUntilUtc`/`LockedBy`, F3-03; `ExhaustedAtUtc`, F3-07).
- `src/Shared.Infrastructure.Persistence/Outbox/OutboxModelConfigurator.cs` (índice compuesto para el claim, incluye `ExhaustedAtUtc` desde F3-07).
- `src/Shared.Infrastructure.Persistence/Outbox/OutboxBatchProcessor.cs`, `OutboxBatchResult.cs`, `OutboxPublisherOptions.cs`, `OutboxPublisherBackgroundService.cs`, `OutboxPublisherServiceCollectionExtensions.cs`.
- `src/Shared.Application/Eventing/EventRetryPolicyOptions.cs`, `EventRetryBackoff.cs`, `EventPublishFailureKind.cs`, `IEventPublishFailureClassifier.cs`, `DefaultEventPublishFailureClassifier.cs`, `EventProcessingExhaustedException.cs` (F3-07).
- `src/Shared.Infrastructure.Messaging.Kafka/KafkaEventPublishFailureClassifier.cs` (F3-07, clasificación específica de Kafka).
- `tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxPublisherIntegrationTests.cs`, `OutboxPublisherRetryTests.cs` (F3-07).
- `tests/Shared.Application.Tests/Eventing/EventRetryBackoffTests.cs` (F3-07).
- `docs/politica-reintentos-eventos.md` (F3-07, política completa, incluido el lado consumidor).
- `docs/guia-eventing-contratos.md` (contratos F3-01/adapter F3-02, tabla `DomainEvent` vs `IIntegrationEvent`).
- `docs/convenciones.md` (regla dura 17, F1-23; regla dura 23, F3-03).
- `docs/adr/0005-mensajeria-kafka.md` (`Accepted` desde F3-02).
