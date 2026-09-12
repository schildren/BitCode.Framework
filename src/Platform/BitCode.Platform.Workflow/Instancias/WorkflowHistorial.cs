using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>
/// Historial de negocio append-only de una <see cref="WorkflowInstance"/> (Fase 6, módulo Workflow,
/// "History") -- DISTINTO de la auditoría genérica de seguridad (<c>IAuditWriter</c>, F2-15 a F2-20):
/// este es el historial de NEGOCIO que un usuario final consulta ("¿qué pasó con mi solicitud?", ver
/// <c>ObtenerHistorialInstanciaQuery</c>), la auditoría es el registro de seguridad interno (quién hizo
/// qué, para investigación/cumplimiento). Es un <see cref="Entity{TId}"/> simple: no levanta eventos
/// propios (el evento de integración ya lo levanta el agregado que originó el hecho -- <c>WorkflowTask</c>/
/// <c>WorkflowInstance</c> -- esta fila es solo la proyección legible del mismo hecho dentro del propio
/// módulo). Nunca se actualiza ni se borra una fila ya escrita (append-only).
/// </summary>
public sealed class WorkflowHistorial : Entity<Guid>, ITenantEntity
{
    public Guid WorkflowInstanceId { get; private set; }

    public DateTime FechaUtc { get; private set; }

    /// <summary>Por ejemplo: "InstanciaIniciada", "TareaCreada", "TareaAprobada", "TareaRechazada",
    /// "TareaDelegada", "TareaEscalada", "InstanciaFinalizada".</summary>
    public string TipoEvento { get; private set; } = string.Empty;

    public string Detalle { get; private set; } = string.Empty;

    /// <summary><see langword="null"/> para un evento generado automáticamente por el motor (por ejemplo,
    /// un avance condicional sin tarea humana, o el escalamiento por SLA de <c>WorkflowEscalamientoJob</c>,
    /// que actúa como el sistema, no como un usuario).</summary>
    public Guid? ActorUserId { get; private set; }

    public Guid TenantId { get; set; }

    public WorkflowHistorial(Guid id, Guid workflowInstanceId, string tipoEvento, string detalle, Guid? actorUserId)
        : base(id)
    {
        WorkflowInstanceId = workflowInstanceId;
        FechaUtc = DateTime.UtcNow;
        TipoEvento = tipoEvento;
        Detalle = detalle;
        ActorUserId = actorUserId;
    }

    private WorkflowHistorial()
    {
    }
}
