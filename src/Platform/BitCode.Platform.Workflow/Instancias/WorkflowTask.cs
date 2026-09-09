using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Instancias;

public enum WorkflowTaskEstado
{
    Pendiente = 0,
    Resuelta = 1,
}

/// <summary>
/// Acción humana pendiente de una <see cref="WorkflowInstance"/> (Fase 6, módulo Workflow) -- se crea al
/// entrar la instancia a un <c>WorkflowState</c> con <c>RequiereTarea = true</c> ("Task"/"Assignment" del
/// Plan Maestro). Es un <see cref="AggregateRoot{TId}"/> propio (levanta
/// <see cref="TareaAsignadaIntegrationEvent"/>/<see cref="TareaAprobadaIntegrationEvent"/>/
/// <see cref="TareaRechazadaIntegrationEvent"/>).
/// </summary>
public sealed class WorkflowTask : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, IHasConcurrencyToken
{
    public Guid WorkflowInstanceId { get; private set; }

    public Guid WorkflowStateId { get; private set; }

    public string Titulo { get; private set; } = string.Empty;

    /// <summary>Asignación actual (Fase 6, "Assignment") -- primer alcance mínimo: siempre un usuario
    /// concreto (no un rol ni un cargo/área de Organization todavía, ver <c>docs/guia-workflow.md</c>,
    /// sección "Pendientes" -- delegar a un cargo/área requeriría una dependencia dura de este módulo
    /// sobre Organization, que el Plan Maestro no exige para el primer alcance).</summary>
    public Guid AsignadoAUserId { get; private set; }

    /// <summary>Asignación original al crear la tarea -- se conserva aunque se delegue o escale, para que
    /// el historial pueda mostrar de quién a quién se movió la tarea.</summary>
    public Guid AsignadoOriginalUserId { get; private set; }

    public WorkflowTaskEstado Estado { get; private set; } = WorkflowTaskEstado.Pendiente;

    public string? AccionResuelta { get; private set; }

    public Guid? ResueltaPorUserId { get; private set; }

    public DateTime? ResueltaAtUtc { get; private set; }

    public string? Comentario { get; private set; }

    /// <summary>Plazo de resolución (Fase 6, "Timeout y SLA") -- <see langword="null"/> = sin SLA, nunca
    /// escalada automáticamente. Ver <see cref="WorkflowState.SlaMinutos"/>.</summary>
    public DateTime? SlaVencimientoUtc { get; private set; }

    /// <summary><see langword="true"/> si <c>WorkflowEscalamientoJob</c> ya reasignó esta tarea por
    /// vencimiento de SLA -- evita que dos corridas del job (o un reintento tras una caída a mitad de
    /// ciclo, ver <c>docs/guia-quartz-ha.md</c> sección 5) la escalen dos veces.</summary>
    public bool Escalada { get; private set; }

    public DateTime? EscaladaAtUtc { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    /// <summary>Token de concurrencia optimista (F1-08, <c>IHasConcurrencyToken</c>) -- necesario en esta
    /// entidad porque puede ser mutada por dos caminos independientes sin ninguna coordinación entre sí:
    /// un actor humano resolviendo/delegando vía HTTP y <c>WorkflowEscalamientoJob</c> escalándola por SLA
    /// vencido. Sin este token, un solape entre ambos caminos pierde en silencio el efecto del que
    /// commitea primero (lost update); con él, el segundo <c>SaveChangesAsync</c> falla con un conflicto
    /// de concurrencia que el framework traduce a <c>Result.Failure</c> con <c>ErrorType.Conflict</c>
    /// (HTTP 409) en vez de pisar el cambio ajeno sin que nadie lo note.</summary>
    public byte[] RowVersion { get; set; } = [];

    public WorkflowTask(
        Guid id, Guid workflowInstanceId, Guid workflowStateId, string titulo, Guid asignadoAUserId,
        DateTime? slaVencimientoUtc)
        : base(id)
    {
        WorkflowInstanceId = workflowInstanceId;
        WorkflowStateId = workflowStateId;
        Titulo = titulo;
        AsignadoAUserId = asignadoAUserId;
        AsignadoOriginalUserId = asignadoAUserId;
        SlaVencimientoUtc = slaVencimientoUtc;

        RaiseDomainEvent(new TareaAsignadaIntegrationEvent(Id, WorkflowInstanceId, AsignadoAUserId));
    }

    private WorkflowTask()
    {
    }

    /// <summary>
    /// Resuelve la tarea con una acción concreta (Fase 6, "Aprobación y rechazo") -- la validación de que
    /// <paramref name="actorUserId"/> es realmente el asignado actual (RBAC/ABAC, "resolver una tarea
    /// ajena debería ser imposible") ocurre ANTES de llamar este método, en
    /// <c>ResolverTareaCommandHandler</c>: este método asume que el llamador ya verificó ownership, y solo
    /// protege la invariante de negocio "una tarea ya resuelta no se vuelve a resolver".
    /// </summary>
    public Result Resolver(string accion, Guid actorUserId, string? comentario)
    {
        if (Estado != WorkflowTaskEstado.Pendiente)
        {
            return Result.Failure(Error.Conflict(
                "Workflow.Tareas.YaResuelta", "La tarea ya fue resuelta anteriormente."));
        }

        Estado = WorkflowTaskEstado.Resuelta;
        AccionResuelta = accion;
        ResueltaPorUserId = actorUserId;
        ResueltaAtUtc = DateTime.UtcNow;
        Comentario = comentario;

        if (string.Equals(accion, "Aprobar", StringComparison.OrdinalIgnoreCase))
        {
            RaiseDomainEvent(new TareaAprobadaIntegrationEvent(Id, WorkflowInstanceId, actorUserId));
        }
        else if (string.Equals(accion, "Rechazar", StringComparison.OrdinalIgnoreCase))
        {
            RaiseDomainEvent(new TareaRechazadaIntegrationEvent(Id, WorkflowInstanceId, actorUserId));
        }

        return Result.Success();
    }

    /// <summary>
    /// Delegación (Fase 6, "Delegación y escalamiento") -- reasigna la tarea a otro actor sin resolverla.
    /// Igual que <see cref="Resolver"/>, la verificación de que el actor que pide delegar es el asignado
    /// actual ocurre en el handler, no acá.
    /// </summary>
    public Result Delegar(Guid nuevoAsignadoUserId)
    {
        if (Estado != WorkflowTaskEstado.Pendiente)
        {
            return Result.Failure(Error.Conflict(
                "Workflow.Tareas.YaResuelta", "No se puede delegar una tarea ya resuelta."));
        }

        if (nuevoAsignadoUserId == AsignadoAUserId)
        {
            return Result.Failure(Error.Validation(
                "Workflow.Tareas.DelegacionInvalida", "El nuevo asignado debe ser distinto del actual."));
        }

        AsignadoAUserId = nuevoAsignadoUserId;

        RaiseDomainEvent(new TareaAsignadaIntegrationEvent(Id, WorkflowInstanceId, nuevoAsignadoUserId));

        return Result.Success();
    }

    /// <summary>
    /// Escalamiento automático por vencimiento de SLA (Fase 6, "Timeout y SLA") -- llamado únicamente por
    /// <c>WorkflowEscalamientoJob</c>. Idempotente por diseño (regla dura 28, docs/convenciones.md): si la
    /// tarea ya fue escalada o ya no está pendiente, no repite el efecto -- una segunda ejecución del job
    /// sobre la misma tarea (reintento tras una caída a mitad de ciclo, o un solape entre disparos) no
    /// duplica la reasignación ni levanta el evento dos veces.
    /// </summary>
    public bool Escalar(Guid nuevoAsignadoUserId)
    {
        if (Estado != WorkflowTaskEstado.Pendiente || Escalada)
        {
            return false;
        }

        AsignadoAUserId = nuevoAsignadoUserId;
        Escalada = true;
        EscaladaAtUtc = DateTime.UtcNow;

        RaiseDomainEvent(new TareaAsignadaIntegrationEvent(Id, WorkflowInstanceId, nuevoAsignadoUserId));

        return true;
    }
}
