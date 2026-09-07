using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Domain.Outbox;

/// <summary>
/// Registro persistido de un evento de dominio levantado por un <c>AggregateRoot&lt;TId&gt;</c>
/// (F1-23, Outbox base): <c>OutboxSaveChangesInterceptor</c> (Shared.Infrastructure.Persistence) lo
/// escribe en el mismo <c>SaveChangesAsync</c> que persiste el cambio de negocio del agregado que lo
/// levantó — nunca en un <c>SaveChanges</c> separado —, así que si el commit tiene éxito el evento ya
/// está persistido junto con el cambio de negocio, y si la transacción hace rollback ninguno de los dos
/// queda persistido (atomicidad real, no dos escrituras independientes que podrían quedar
/// inconsistentes entre sí).
/// </summary>
/// <remarks>
/// Esta tarea (F1-23) cubre únicamente el lado emisor: persistir el evento junto al cambio de negocio.
/// El relay/publisher que lea las filas con <see cref="ProcessedAtUtc"/> nulo y las publique a un
/// broker externo (Kafka, ver ADR <c>docs/adr/0005-mensajeria-kafka.md</c>, todavía "Proposed") es
/// trabajo de Fase 3 — no lo implementa este tipo ni <c>OutboxSaveChangesInterceptor</c>.
/// <see cref="RetryCount"/> y <see cref="Error"/> existen para que ese relay futuro no requiera un
/// cambio de esquema; F1-23 no los usa desde ningún código propio.
///
/// Implementa <see cref="ITenantEntity"/> con el mismo criterio que <c>IdempotencyKey</c> (F1-22,
/// regla dura 7 de <c>docs/convenciones.md</c>): el evento pertenece al tenant del scope que lo generó,
/// y el filtro global de <c>MultiTenancyModelConfigurator</c> restringe cualquier lectura futura (por
/// ejemplo, la del relay de Fase 3) al tenant correspondiente salvo que use
/// <c>IgnoreQueryFilters()</c> explícitamente (necesario para un relay que procesa todos los tenants).
/// </remarks>
public sealed class OutboxMessage : ITenantEntity
{
    public Guid Id { get; init; }

    public Guid TenantId { get; set; }

    /// <summary>
    /// Nombre calificado de ensamblado (<c>Type.AssemblyQualifiedName</c>) del tipo concreto de
    /// <c>DomainEvent</c> serializado en <see cref="PayloadJson"/> — necesario para poder
    /// deserializarlo de vuelta a su tipo original sin depender de una tabla de mapeo adicional.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>JSON del evento de dominio, serializado con su tipo concreto (no la clase base <c>DomainEvent</c>).</summary>
    public required string PayloadJson { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    /// <summary>
    /// <see langword="null"/> mientras el evento no fue publicado por el relay (Fase 3). Esta tarea
    /// (F1-23) nunca la establece: solo escribe la fila con este campo en <see langword="null"/>.
    /// </summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>Reservado para el relay de Fase 3 (reintentos de publicación); F1-23 no lo incrementa.</summary>
    public int RetryCount { get; set; }

    /// <summary>Reservado para el relay de Fase 3 (motivo del último fallo de publicación); F1-23 no lo establece.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// F3-03 (Outbox Publisher): mientras es mayor que <see cref="DateTime.UtcNow"/>, esta fila está
    /// "reclamada" por la instancia de <see cref="LockedBy"/> — otra instancia del worker (varias
    /// réplicas del proceso, ya anticipado por la Fase 4 de alta disponibilidad) no puede volver a
    /// reclamarla mientras el lock siga vigente (ver <c>OutboxBatchProcessor.ClaimBatchAsync</c>, que
    /// usa <c>UPDLOCK, READPAST</c> para que dos instancias concurrentes nunca reclamen la misma fila
    /// a la vez). Vuelve a <see langword="null"/> en cuanto la fila se marca como procesada, o se
    /// limpia explícitamente si la publicación de esa fila falla (para que el siguiente ciclo de
    /// sondeo pueda reintentarla sin esperar a que el lock expire). Si el proceso muere sin llegar a
    /// limpiarlo, el lock expira solo (por tiempo) y otra instancia puede reclamar la fila.
    /// </summary>
    public DateTime? LockedUntilUtc { get; set; }

    /// <summary>Identificador de la instancia de worker que reclamó esta fila (ver <see cref="LockedUntilUtc"/>); solo para diagnóstico/observabilidad, F3-03.</summary>
    public string? LockedBy { get; set; }
}
