namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Datos de entrada para registrar una nueva entrada de auditoría (F2-15, Épica F2-D) vía
/// <see cref="IAuditWriter.WriteAsync"/>. Separado de <see cref="AuditEntry"/> a propósito: el llamador
/// nunca decide <see cref="AuditEntry.Id"/>, <see cref="AuditEntry.OccurredAtUtc"/> ni
/// <see cref="AuditEntry.AuditHash"/> -- esos tres campos los calcula siempre <see cref="IAuditWriter"/>
/// en el momento de escribir, para que ningún consumidor pueda fabricar un timestamp o un hash a mano y
/// entregar un registro ya "manipulado" antes de que exista.
/// </summary>
public sealed class AuditEntryRequest
{
    public AuditEntryRequest(
        AuditActor actor,
        Guid? tenantId,
        string action,
        AuditResource resource,
        AuditOutcome outcome,
        string? reason = null,
        string? correlationId = null,
        string? traceId = null,
        string? ipAddress = null,
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(resource);

        Actor = actor;
        TenantId = tenantId;
        Action = action;
        Resource = resource;
        Outcome = outcome;
        Reason = reason;
        CorrelationId = correlationId;
        TraceId = traceId;
        IpAddress = ipAddress;
        Metadata = metadata ?? new Dictionary<string, string?>();
    }

    public AuditActor Actor { get; }

    /// <summary>
    /// Tenant/empresa de la operación auditada (coherente con <c>ITenantContext</c>, F1-15). <see
    /// langword="null"/> únicamente para un proyecto sin multi-tenancy habilitada o para una operación de
    /// alcance verdaderamente global (por ejemplo, una acción de plataforma sin tenant asociado) -- nunca
    /// como forma de "saltear" la resolución de tenant de una operación que sí lo tiene.
    /// </summary>
    public Guid? TenantId { get; }

    /// <summary>
    /// Acción ejecutada, misma convención <c>"{entidad}.{accion}"</c> que un permiso RBAC (F2-07,
    /// <c>docs/convenciones.md</c>) para que la auditoría sea directamente correlacionable con el permiso
    /// evaluado.
    /// </summary>
    public string Action { get; }

    public AuditResource Resource { get; }

    public AuditOutcome Outcome { get; }

    /// <summary>
    /// Motivo, obligatorio en la práctica cuando <see cref="Outcome"/> es <see cref="AuditOutcome.Denied"/>
    /// (por ejemplo, el código de <c>AbacDecisionReasons</c>/<c>Error.Code</c> que produjo la denegación) o
    /// <see cref="AuditOutcome.Error"/> (una descripción corta del fallo) -- no se valida como requerido a
    /// nivel de tipo porque un <see cref="AuditOutcome.Success"/> normalmente no lo necesita.
    /// </summary>
    public string? Reason { get; }

    /// <summary>Identificador de correlación de negocio (por ejemplo, el mismo usado en Idempotency-Key/logs).</summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// Identificador de traza para correlacionar con telemetría/logs distribuidos (por ejemplo,
    /// <c>Activity.Current?.Id</c>/<c>TraceId</c> de OpenTelemetry, si el proyecto ya lo tiene
    /// instrumentado) -- este módulo no resuelve el valor automáticamente, lo recibe ya resuelto del
    /// llamador.
    /// </summary>
    public string? TraceId { get; }

    public string? IpAddress { get; }

    /// <summary>
    /// Contexto adicional específico del caso de uso (valores de texto simple, no objetos arbitrarios, para
    /// que <see cref="AuditHashCalculator"/> pueda serializarlos de forma determinística). Nunca debe
    /// contener PII/datos sensibles sin redactar -- la política de redacción formal es F2-19; hasta
    /// entonces, es responsabilidad del llamador no volcar acá un dato que no debería persistir en texto
    /// plano en un registro de auditoría de retención larga.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Metadata { get; }
}
