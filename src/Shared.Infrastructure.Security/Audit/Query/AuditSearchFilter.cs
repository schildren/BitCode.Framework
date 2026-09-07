using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Query;

/// <summary>
/// Filtros acotados de una búsqueda controlada de auditoría (F2-20, Épica F2-D). Deliberadamente NO
/// expone ningún camino para "traer todo sin filtro/paginación" (coherente con la prohibición de
/// <c>IQueryable</c> expuesto de <c>docs/convenciones.md</c>): <see cref="Page"/> es obligatorio y
/// reutiliza <see cref="PageRequest"/> (F1-21, ya valida <c>page</c>/<c>pageSize</c> contra un máximo)
/// en lugar de que este módulo reinvente su propia primitiva de paginación.
/// </summary>
public sealed class AuditSearchFilter
{
    public AuditSearchFilter(
        PageRequest page,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        Guid? tenantId = null,
        string? actorId = null,
        string? action = null,
        AuditOutcome? outcome = null,
        string? resourceType = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        Page = page;
        FromUtc = fromUtc;
        ToUtc = toUtc;
        TenantId = tenantId;
        ActorId = actorId;
        Action = action;
        Outcome = outcome;
        ResourceType = resourceType;
    }

    public PageRequest Page { get; }

    /// <summary>Límite inferior (inclusive) sobre <see cref="AuditEntry.OccurredAtUtc"/>. <see langword="null"/> = sin límite.</summary>
    public DateTime? FromUtc { get; }

    /// <summary>Límite superior (inclusive) sobre <see cref="AuditEntry.OccurredAtUtc"/>. <see langword="null"/> = sin límite.</summary>
    public DateTime? ToUtc { get; }

    /// <summary>
    /// Tenant al que restringir la búsqueda. <see langword="null"/> = sin restricción para
    /// <see cref="IAuditReader"/> (uso interno/de plataforma); <see cref="AuditQueryService"/>, en cambio,
    /// EXIGE que coincida con el tenant del llamador cuando la multi-tenancy está habilitada -- ver
    /// <c>docs/guia-auditoria-inmutable.md</c>, sección F2-20.
    /// </summary>
    public Guid? TenantId { get; }

    public string? ActorId { get; }

    /// <summary>Acción exacta (misma convención <c>"{entidad}.{accion}"</c> que un permiso RBAC).</summary>
    public string? Action { get; }

    public AuditOutcome? Outcome { get; }

    public string? ResourceType { get; }
}
