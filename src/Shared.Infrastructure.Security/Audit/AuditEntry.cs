namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Una entrada de auditoría inmutable (F2-15, Épica F2-D): actor, tenant, acción, recurso, resultado y
/// hash del propio registro. "Append-only" se garantiza a nivel del tipo, no solo de configuración de
/// base de datos: todas las propiedades son de solo lectura (<c>get</c> sin <c>set</c>/<c>init</c>) y ni
/// este tipo ni <see cref="IAuditWriter"/> exponen ningún método de actualización o eliminación -- una vez
/// construida, una <see cref="AuditEntry"/> no tiene ninguna forma de mutarse. El constructor es público
/// (mismo criterio que el resto de los value object del framework, por ejemplo <c>AbacResource</c>) pero
/// <see cref="Id"/>/<see cref="OccurredAtUtc"/>/<see cref="AuditHash"/> están pensados para que solo los
/// calcule <see cref="IAuditWriter"/> en el momento de escribir -- un llamador de aplicación nunca debería
/// construir una <see cref="AuditEntry"/> directamente, solo llamar <see cref="IAuditWriter.WriteAsync"/>.
/// </summary>
public sealed class AuditEntry
{
    public AuditEntry(
        Guid id,
        DateTime occurredAtUtc,
        AuditActor actor,
        Guid? tenantId,
        string action,
        AuditResource resource,
        AuditOutcome outcome,
        string? reason,
        string? correlationId,
        string? traceId,
        string? ipAddress,
        IReadOnlyDictionary<string, string?> metadata,
        string auditHash,
        string? previousAuditHash = null)
    {
        Id = id;
        OccurredAtUtc = occurredAtUtc;
        Actor = actor;
        TenantId = tenantId;
        Action = action;
        Resource = resource;
        Outcome = outcome;
        Reason = reason;
        CorrelationId = correlationId;
        TraceId = traceId;
        IpAddress = ipAddress;
        Metadata = metadata;
        AuditHash = auditHash;
        PreviousAuditHash = previousAuditHash;
    }

    /// <summary>Identificador único del registro, generado por <see cref="IAuditWriter"/> -- nunca provisto por el llamador.</summary>
    public Guid Id { get; }

    /// <summary>Timestamp UTC en el que <see cref="IAuditWriter"/> escribió el registro.</summary>
    public DateTime OccurredAtUtc { get; }

    public AuditActor Actor { get; }

    public Guid? TenantId { get; }

    public string Action { get; }

    public AuditResource Resource { get; }

    public AuditOutcome Outcome { get; }

    public string? Reason { get; }

    public string? CorrelationId { get; }

    public string? TraceId { get; }

    public string? IpAddress { get; }

    public IReadOnlyDictionary<string, string?> Metadata { get; }

    /// <summary>
    /// Hash SHA-256 (hex, mayúsculas) calculado por <see cref="AuditHashCalculator"/> sobre todos los
    /// campos críticos de ESTE registro (<see cref="Id"/>, <see cref="OccurredAtUtc"/>, <see cref="Actor"/>,
    /// <see cref="TenantId"/>, <see cref="Action"/>, <see cref="Resource"/>, <see cref="Outcome"/>, <see
    /// cref="Reason"/>, <see cref="CorrelationId"/>, <see cref="TraceId"/>, <see cref="IpAddress"/>, <see
    /// cref="Metadata"/>). Determinístico para el mismo contenido y sensible a cualquier cambio en
    /// cualquiera de esos campos -- la propiedad que F2-16 necesita para detectar manipulación posterior de
    /// un registro ya escrito (comparando el hash recalculado contra el persistido).
    /// </summary>
    public string AuditHash { get; }

    /// <summary>
    /// Reservado para F2-16 (cadena de integridad): hash del registro de auditoría inmediatamente anterior
    /// de la misma cadena, para poder detectar la eliminación/reordenamiento de un registro completo (algo
    /// que el hash de un registro aislado, por sí solo, no detecta). F2-15 no calcula ningún encadenamiento
    /// todavía -- esta propiedad queda siempre en <see langword="null"/> hasta que el servicio de
    /// integridad de F2-16 la complete; se deja definida ahora para no requerir un cambio de esquema/
    /// contrato público breaking cuando F2-16 se implemente.
    /// </summary>
    public string? PreviousAuditHash { get; }
}
