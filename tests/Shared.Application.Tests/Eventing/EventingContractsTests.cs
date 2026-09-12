using BitCode.Framework.Shared.Application.Eventing;
using FluentAssertions;
using NSubstitute;

namespace BitCode.Framework.Shared.Application.Tests.Eventing;

/// <summary>
/// Pruebas de forma/contrato de F3-01 (Contratos: <see cref="IIntegrationEvent"/>,
/// <see cref="IEventPublisher"/>, <see cref="IEventConsumer{TEvent}"/>) — no requieren ningún broker
/// real ni infraestructura externa, coherente con el criterio de aceptación "Sin dependencia al
/// proveedor": estos contratos viven en Shared.Application, que no referencia ningún paquete de
/// broker.
/// </summary>
public class EventingContractsTests
{
    [Fact]
    public void IntegrationEvent_asigna_EventId_unico_por_instancia()
    {
        var first = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Ada Lovelace");
        var second = new TestOrderCreatedIntegrationEvent(first.OrderId, first.CustomerName);

        first.EventId.Should().NotBe(Guid.Empty);
        second.EventId.Should().NotBe(Guid.Empty);
        first.EventId.Should().NotBe(second.EventId, "cada instancia de evento debe ser identificable de forma única para deduplicación (Inbox, F1-24/F3-04)");
    }

    [Fact]
    public void IntegrationEvent_asigna_OccurredOnUtc_al_momento_de_construccion()
    {
        var before = DateTime.UtcNow;
        var evento = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Ada Lovelace");
        var after = DateTime.UtcNow;

        evento.OccurredOnUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void IntegrationEvent_expone_EventType_declarado_por_el_evento_concreto()
    {
        var evento = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Ada Lovelace");

        evento.EventType.Should().Be("Pedidos.PedidoCreado");
    }

    [Fact]
    public void IntegrationEvent_SchemaVersion_por_defecto_es_1()
    {
        var evento = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Ada Lovelace");

        evento.SchemaVersion.Should().Be(1);
    }

    [Fact]
    public void IntegrationEvent_permite_sobrescribir_SchemaVersion_para_una_evolucion_de_esquema()
    {
        var evento = new TestOrderCreatedV2IntegrationEvent(Guid.NewGuid(), "Ada Lovelace", 199.90m);

        evento.SchemaVersion.Should().Be(2);
        evento.EventType.Should().Be("Pedidos.PedidoCreado", "el nombre lógico del evento no cambia entre versiones de esquema compatibles, solo SchemaVersion");
    }

    [Fact]
    public void IIntegrationEvent_puede_implementarse_directamente_sin_heredar_de_IntegrationEvent()
    {
        var originalEventId = Guid.NewGuid();
        var originalOccurredOnUtc = DateTime.UtcNow.AddMinutes(-5);

        IIntegrationEvent evento = new TestDeserializedIntegrationEvent(
            originalEventId,
            originalOccurredOnUtc,
            "Pedidos.PedidoCreado",
            SchemaVersion: 1,
            OrderId: Guid.NewGuid());

        evento.EventId.Should().Be(originalEventId, "un evento reconstruido por deserialización debe conservar el EventId original del productor, no generar uno nuevo");
        evento.OccurredOnUtc.Should().Be(originalOccurredOnUtc);
    }

    [Fact]
    public async Task IEventPublisher_expone_PublishAsync_para_un_unico_evento()
    {
        var publisher = Substitute.For<IEventPublisher>();
        IIntegrationEvent evento = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Ada Lovelace");

        await publisher.PublishAsync(evento);

        await publisher.Received(1).PublishAsync(evento, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IEventPublisher_expone_PublishAsync_para_un_lote_de_eventos()
    {
        var publisher = Substitute.For<IEventPublisher>();
        var eventos = new IIntegrationEvent[]
        {
            new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Ada Lovelace"),
            new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Alan Turing"),
        };

        await publisher.PublishAsync(eventos);

        await publisher.Received(1).PublishAsync(eventos, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IHasPartitionKey_expone_AggregateId_como_clave_de_particion_cuando_el_orden_es_por_entidad_de_negocio()
    {
        var orderId = Guid.NewGuid();
        IIntegrationEvent evento = new TestOrderCreatedWithAggregateIdPartitionKeyIntegrationEvent(orderId, "Ada Lovelace");

        evento.Should().BeAssignableTo<IHasPartitionKey>("un evento que necesita orden por AggregateId (F3-05) declara la clave de partición implementando esta interfaz opcional");
        ((IHasPartitionKey)evento).PartitionKey.Should().Be(orderId.ToString());
    }

    [Fact]
    public void IHasPartitionKey_expone_TenantId_como_clave_de_particion_cuando_el_orden_es_por_tenant()
    {
        var tenantId = Guid.NewGuid();
        IIntegrationEvent evento = new TestOrderCreatedWithTenantIdPartitionKeyIntegrationEvent(Guid.NewGuid(), tenantId);

        evento.Should().BeAssignableTo<IHasPartitionKey>();
        ((IHasPartitionKey)evento).PartitionKey.Should().Be(tenantId.ToString());
    }

    [Fact]
    public void IIntegrationEvent_sin_IHasPartitionKey_sigue_siendo_un_evento_valido_sin_clave_de_particion_explicita()
    {
        IIntegrationEvent evento = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Ada Lovelace");

        evento.Should().NotBeAssignableTo<IHasPartitionKey>("IHasPartitionKey es opcional (F3-05): un evento que no la implementa sigue siendo un IIntegrationEvent válido, sin ninguna garantía de orden entre instancias relacionadas");
    }

    [Fact]
    public async Task IEventConsumer_expone_ConsumeAsync_tipado_al_evento_concreto()
    {
        var consumer = Substitute.For<IEventConsumer<TestOrderCreatedIntegrationEvent>>();
        var evento = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Ada Lovelace");

        await consumer.ConsumeAsync(evento);

        await consumer.Received(1).ConsumeAsync(evento, Arg.Any<CancellationToken>());
    }
}
