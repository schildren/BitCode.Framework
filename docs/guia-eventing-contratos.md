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

## Referencias

- `src/Shared.Application/Eventing/IIntegrationEvent.cs`, `IntegrationEvent.cs`, `IEventPublisher.cs`, `IEventConsumer.cs`.
- `tests/Shared.Application.Tests/Eventing/EventingContractsTests.cs`, `TestIntegrationEvents.cs`.
- `docs/convenciones.md` (regla dura 17/18, Outbox/Inbox, F1-23/F1-24).
- `docs/politica-versionado.md` (versión de esquema de eventos de integración).
- ADR `docs/adr/0005-mensajeria-kafka.md` (todavía `Proposed`).
