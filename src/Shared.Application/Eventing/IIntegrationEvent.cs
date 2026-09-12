namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// Contrato de un evento de integración (F3-01): un hecho de negocio que cruza el límite de un
/// bounded context, distinto de un <c>DomainEvent</c> (Shared.Kernel, F1-23), que es interno al
/// agregado que lo levanta y solo se persiste como fila <c>OutboxMessage</c>. Un
/// <see cref="IIntegrationEvent"/> es el contrato PÚBLICO que otro bounded context puede consumir vía
/// <see cref="IEventConsumer{TEvent}"/> — cambiar su forma es un cambio de contrato público sujeto a
/// <c>docs/politica-versionado.md</c>, no un detalle interno de implementación.
/// </summary>
/// <remarks>
/// Esta interfaz no depende de ningún proveedor de mensajería concreto (Kafka u otro broker,
/// ADR <c>docs/adr/0005-mensajeria-kafka.md</c>, todavía "Proposed") ni de ningún mecanismo de
/// serialización: es el paquete de contratos agnóstico de proveedor que la Fase 3 completa exige
/// como base (criterio de aceptación de F3-01, "Sin dependencia al proveedor"). El adapter concreto
/// hacia un broker (F3-02) y el relay que lea <c>OutboxMessage</c>/escriba <c>InboxMessage</c> (F3-03,
/// F3-04) se apoyan en este contrato sin que este proyecto (Shared.Application) referencie ningún
/// paquete de broker.
///
/// <see cref="EventType"/> es el nombre lógico y estable del evento (por ejemplo,
/// <c>"Pedidos.PedidoCreado"</c>) usado para enrutamiento/nombre de tópico — deliberadamente
/// independiente del nombre del tipo .NET concreto (a diferencia de
/// <c>OutboxMessage.EventType</c>, que hoy guarda el <c>Type.AssemblyQualifiedName</c> del
/// <c>DomainEvent</c> interno) para no acoplar el contrato externo a un detalle de implementación
/// del lenguaje que un consumidor de otro bounded context —potencialmente en otra tecnología— no
/// puede ni debe conocer.
///
/// <see cref="SchemaVersion"/> es el campo explícito de versión de esquema que exige
/// <c>docs/politica-versionado.md</c> ("todo evento de integración lleva un campo explícito de
/// versión de esquema... nunca se infiere la versión del payload por heurística"). La política de
/// compatibilidad forward/backward concreta sobre este campo es trabajo de F3-06; F3-01 solo reserva
/// el campo en el contrato.
///
/// La clave de partición (F3-05, "AggregateId o TenantId según orden requerido") NO es un campo de esta
/// interfaz: un evento concreto que necesita declarar orden explícito implementa adicionalmente
/// <see cref="IHasPartitionKey"/> (interfaz opcional, sin romper este contrato ni ningún consumidor que
/// no la conozca) — ver <c>docs/guia-eventing-contratos.md</c>, sección "Particionamiento (F3-05)".
/// </remarks>
public interface IIntegrationEvent
{
    /// <summary>Identificador único de esta instancia del evento — clave natural de deduplicación del lado consumidor (Inbox, F1-24/F3-04).</summary>
    Guid EventId { get; }

    /// <summary>Momento (UTC) en que ocurrió el hecho de negocio que representa el evento, no el momento en que se publicó o se consumió.</summary>
    DateTime OccurredOnUtc { get; }

    /// <summary>Nombre lógico y estable del evento, usado para enrutamiento/nombre de tópico — independiente del nombre del tipo .NET concreto.</summary>
    string EventType { get; }

    /// <summary>Versión del esquema del evento (ver <c>docs/politica-versionado.md</c>). Un cambio incompatible de forma exige incrementar este valor, nunca mutar el esquema de una versión ya publicada.</summary>
    int SchemaVersion { get; }
}
