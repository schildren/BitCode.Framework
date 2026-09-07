using BitCode.Framework.Shared.Application.Eventing;

namespace BitCode.Framework.Shared.Application.Tests.Eventing;

/// <summary>Evento de integración de ejemplo construido a partir de <see cref="IntegrationEvent"/> (uso típico, F3-01).</summary>
public sealed record TestOrderCreatedIntegrationEvent(Guid OrderId, string CustomerName) : IntegrationEvent
{
    public override string EventType => "Pedidos.PedidoCreado";
}

/// <summary>Evento de integración que sobrescribe <see cref="IntegrationEvent.SchemaVersion"/> (evolución de esquema, F3-06).</summary>
public sealed record TestOrderCreatedV2IntegrationEvent(Guid OrderId, string CustomerName, decimal Total) : IntegrationEvent
{
    public override string EventType => "Pedidos.PedidoCreado";

    public override int SchemaVersion => 2;
}

/// <summary>Evento de integración implementado directamente sobre <see cref="IIntegrationEvent"/> (sin heredar de <see cref="IntegrationEvent"/>), por ejemplo el caso de un evento reconstruido por deserialización con los valores originales del productor.</summary>
public sealed record TestDeserializedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    string EventType,
    int SchemaVersion,
    Guid OrderId) : IIntegrationEvent;

/// <summary>Evento de integración que implementa <see cref="IHasPartitionKey"/> (F3-05) usando el AggregateId (OrderId) como clave de partición — caso "orden por entidad de negocio".</summary>
public sealed record TestOrderCreatedWithAggregateIdPartitionKeyIntegrationEvent(Guid OrderId, string CustomerName)
    : IntegrationEvent, IHasPartitionKey
{
    public override string EventType => "Pedidos.PedidoCreado";

    public string PartitionKey => OrderId.ToString();
}

/// <summary>Evento de integración que implementa <see cref="IHasPartitionKey"/> (F3-05) usando el TenantId como clave de partición — caso "orden por tenant, sin importar el agregado".</summary>
public sealed record TestOrderCreatedWithTenantIdPartitionKeyIntegrationEvent(Guid OrderId, Guid TenantId)
    : IntegrationEvent, IHasPartitionKey
{
    public override string EventType => "Pedidos.PedidoCreado";

    public string PartitionKey => TenantId.ToString();
}
