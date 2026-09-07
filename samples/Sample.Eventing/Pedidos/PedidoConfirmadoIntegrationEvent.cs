using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace Sample.Eventing.Pedidos;

/// <summary>
/// F3-13 (prueba de referencia): evento de integración público del Módulo A (Pedidos) — el hecho de
/// negocio "un pedido fue confirmado" cruza el límite del bounded context y lo puede consumir
/// cualquier otro módulo (en este ejemplo, Facturación, Módulo B, ver
/// <c>Sample.Eventing.Facturacion.PedidoConfirmadoEventConsumer</c>).
/// </summary>
/// <remarks>
/// Implementa DELIBERADAMENTE tanto <see cref="DomainEvent"/> (Shared.Kernel, F1-23 — para que
/// <c>OutboxSaveChangesInterceptor</c> lo recolecte del <c>AggregateRoot&lt;TId&gt;</c> que lo levanta,
/// <see cref="Pedido"/>) como <see cref="IIntegrationEvent"/> (Shared.Application, F3-01 — el contrato
/// que <c>OutboxBatchProcessor</c>, F3-03, usa para decidir qué fila de <c>OutboxMessage</c> cruza
/// hacia <c>IEventPublisher</c> en vez de quedar marcada como "interna" sin publicarse): mismo patrón
/// ya usado por <c>OutboxPublisherTestEvent</c>
/// (Shared.Infrastructure.Persistence.Tests/Integration/OutboxPublisherIntegrationTests.cs).
///
/// Implementa <see cref="IHasPartitionKey"/> (F3-05) con <see cref="PedidoId"/> como clave de
/// partición: todos los eventos de un mismo pedido quedan en la misma partición de Kafka, así que un
/// consumidor que necesite orden entre eventos del mismo agregado lo obtiene sin trabajo adicional.
///
/// Este proyecto (Sample.Eventing) NO referencia
/// <c>BitCode.Framework.Shared.Infrastructure.Messaging.Kafka</c> — ver
/// <c>Sample.Eventing.csproj</c> y el Gate de salida de la Fase 3 ("No existe dependencia de dominio
/// hacia Kafka", verificado acá a nivel de referencia de proyecto, no solo de convención).
/// </remarks>
public sealed record PedidoConfirmadoIntegrationEvent(Guid PedidoId, string Cliente, decimal Monto)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Pedidos.PedidoConfirmado";

    public int SchemaVersion => 1;

    public string PartitionKey => PedidoId.ToString();
}
