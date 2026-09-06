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
    /// Hash del registro de auditoría inmediatamente anterior de la misma cadena (F2-16, Épica F2-D), para
    /// poder detectar la eliminación/reordenamiento de un registro completo -- algo que el hash de un
    /// registro aislado (<see cref="AuditHash"/>), por sí solo, no detecta. <see langword="null"/> para el
    /// registro génesis de una cadena (el primero escrito).
    /// <para>
    /// "Misma cadena" es <b>por tenant</b>: <see cref="IAuditWriter"/> mantiene una cadena de integridad
    /// independiente por cada valor distinto de <see cref="TenantId"/> (incluida una cadena propia para
    /// <see cref="TenantId"/> <see langword="null"/>, operaciones de plataforma sin tenant). Esta es una
    /// decisión deliberada de F2-16, no un detalle de implementación: la auditoría de tenants distintos ya
    /// es lógicamente independiente entre sí (ningún caso de uso necesita verificar la integridad conjunta
    /// de auditoría de dos tenants distintos en una sola cadena), y una cadena única global mezclaría el
    /// orden de escritura de operaciones de tenants sin relación entre sí, dificultando la verificación
    /// (<see cref="IAuditIntegrityVerifier"/>) y la futura exportación WORM (F2-18) por tenant.
    /// </para>
    /// </summary>
    public string? PreviousAuditHash { get; }
}
