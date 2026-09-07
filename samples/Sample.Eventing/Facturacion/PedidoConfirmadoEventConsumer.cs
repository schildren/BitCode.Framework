using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Persistence;
using Sample.Eventing.Pedidos;

namespace Sample.Eventing.Facturacion;

/// <summary>
/// F3-13 (prueba de referencia): handler real de negocio del Módulo B (Facturación) para el evento de
/// integración público del Módulo A (Pedidos) — se resuelve por <c>KafkaEventConsumer&lt;TEvent&gt;</c>
/// (F3-02/F3-04) del mismo scope de DI que <c>IInboxMessageProcessor</c>, así que el
/// <c>SaveChangesAsync</c> que persiste esta <see cref="FacturaPendiente"/> y el que marca la fila
/// <c>InboxMessage</c> como procesada son el MISMO SaveChangesAsync (mismo DbContext de scope) —
/// duplicados (mismo <c>EventId</c> reentregado) se descartan antes de llegar acá (F3-04, "duplicados
/// no repiten efectos").
/// </summary>
/// <remarks>
/// No referencia ningún tipo de <c>Shared.Infrastructure.Messaging.Kafka</c>: este consumidor de
/// negocio es agnóstico del broker (Gate de salida de la Fase 3, "No existe dependencia de dominio
/// hacia Kafka") — el adapter Kafka concreto vive en la composición/test que lo invoca.
/// </remarks>
public class PedidoConfirmadoEventConsumer(IRepository<FacturaPendiente, Guid> repository)
    : IEventConsumer<PedidoConfirmadoIntegrationEvent>
{
    public async Task ConsumeAsync(PedidoConfirmadoIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var facturaPendiente = new FacturaPendiente(
            Guid.NewGuid(),
            integrationEvent.PedidoId,
            integrationEvent.Cliente,
            integrationEvent.Monto);

        await repository.AddAsync(facturaPendiente, cancellationToken);
    }
}
