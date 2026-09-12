namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// Interfaz opcional (F3-05, "Particionamiento") que un <see cref="IIntegrationEvent"/> concreto puede
/// implementar para declarar explícitamente su clave de partición — el estándar de particionamiento que
/// exige el Plan Maestro (Fase 3, fila F3-05: "Definir AggregateId o TenantId según orden requerido") y
/// la semántica exigida de la Fase 3 ("Orden: solo garantizado dentro de la partición definida").
/// </summary>
/// <remarks>
/// <para>
/// Deliberadamente NO es un campo obligatorio de <see cref="IIntegrationEvent"/>: distintos eventos de
/// integración necesitan distinto criterio de orden (o ninguno en absoluto), y forzar un único campo
/// (<c>AggregateId</c> u otra convención fija) en el contrato base habría sido tanto una decisión
/// prematura como un cambio de contrato público retroactivo sobre F3-01 — <see cref="IHasPartitionKey"/>
/// es una interfaz adicional que un evento concreto puede implementar sin tocar
/// <see cref="IIntegrationEvent"/> ni ningún consumidor/publisher/serializador ya existente (F3-01 a
/// F3-04 siguen compilando y pasando sin cambios: un evento que no la implementa se comporta
/// exactamente igual que antes de F3-05).
/// </para>
/// <para>
/// <b>Cuándo usar el <c>AggregateId</c> del agregado que originó el evento como <see cref="PartitionKey"/>:</b>
/// cuando el orden relevante es "entre eventos del mismo agregado de negocio" — por ejemplo,
/// <c>PedidoCreado</c> y <c>PedidoCancelado</c> del mismo pedido deben llegar a cualquier consumidor en
/// ese orden exacto, sin que importe el orden relativo frente a eventos de OTROS pedidos. El
/// <c>Id</c> (convertido a <see cref="string"/>) del <c>AggregateRoot&lt;TId&gt;</c> (Shared.Kernel,
/// F1-23) que levantó el <c>DomainEvent</c> del que se reconstruyó este evento de integración
/// (F3-03, <c>OutboxBatchProcessor</c>) es la elección natural.
/// </para>
/// <para>
/// <b>Cuándo usar el <c>TenantId</c> como <see cref="PartitionKey"/>:</b> cuando no importa el orden
/// relativo entre agregados distintos, pero sí que ningún evento de un tenant se procese fuera de orden
/// respecto de otro evento del MISMO tenant (por ejemplo, un proyector de reportes por tenant que
/// necesita ver los eventos de ese tenant en el orden en que ocurrieron, sin que la intercalación con
/// eventos de otros tenants dentro de la misma partición pueda romper esa garantía). <c>TenantId</c>
/// (<c>ITenantEntity.TenantId</c>, Shared.Domain/Shared.Kernel) del origen del evento es la elección
/// natural en ese caso — es una decisión del autor del evento concreto, no una regla única y universal
/// que este framework pueda imponer.
/// </para>
/// <para>
/// Si un evento NO implementa esta interfaz, <c>KafkaEventPublisher</c> (F3-02/F3-05) sigue usando
/// <see cref="IIntegrationEvent.EventId"/> como <c>Key</c> del mensaje Kafka (comportamiento heredado de
/// F3-02, sin cambios) — SIN ninguna garantía de orden entre eventos relacionados: al ser un valor
/// distinto por instancia, cada evento puede terminar en cualquier partición del tópico de forma
/// efectivamente aleatoria.
/// </para>
/// </remarks>
public interface IHasPartitionKey
{
    /// <summary>
    /// Clave de partición explícita de este evento. Dos eventos con la misma <see cref="PartitionKey"/>
    /// quedan en la MISMA partición del tópico Kafka (particionamiento por key del broker: mismo hash de
    /// key → misma partición) y, por lo tanto, un consumidor de esa partición los recibe en el mismo
    /// orden relativo en que se publicaron. No garantiza nada sobre el orden relativo frente a eventos
    /// con una <see cref="PartitionKey"/> distinta — semántica exigida de la Fase 3: "orden solo
    /// garantizado dentro de la partición definida".
    /// </summary>
    string PartitionKey { get; }
}
