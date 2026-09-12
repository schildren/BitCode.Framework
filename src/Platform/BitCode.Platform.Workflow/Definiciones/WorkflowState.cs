using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

/// <summary>
/// Un nodo del grafo de una <see cref="WorkflowVersion"/> (Fase 6, módulo Workflow) -- por ejemplo,
/// "Pendiente", "EnRevision", "Aprobado", "Rechazado". Es un <see cref="Entity{TId}"/> simple (no
/// <see cref="AggregateRoot{TId}"/>): no levanta eventos propios, su ciclo de vida está atado por completo
/// a la versión que lo contiene -- pero conserva <see cref="ITenantEntity"/> porque tiene su propia
/// tabla/DbSet con filtro global de tenant, accedida vía su propio <c>IRepository&lt;WorkflowState,
/// Guid&gt;</c> (regla dura 1, nunca vía navegación de <see cref="WorkflowVersion"/>).
/// </summary>
public sealed class WorkflowState : Entity<Guid>, ITenantEntity
{
    public Guid WorkflowVersionId { get; private set; }

    /// <summary>Código lógico único DENTRO de la versión (por ejemplo, "PENDIENTE") -- lo que
    /// <see cref="WorkflowTransition.DesdeEstadoId"/>/<see cref="WorkflowTransition.HaciaEstadoId"/>
    /// referencian por <c>Id</c>, no por este código; el código es solo para que el consumidor de la API
    /// arme el grafo por nombre legible al crear la versión (ver <c>CrearWorkflowVersionCommand</c>).</summary>
    public string Codigo { get; private set; } = string.Empty;

    public string Nombre { get; private set; } = string.Empty;

    /// <summary>Exactamente un <see cref="WorkflowState"/> de la versión tiene <see cref="EsInicial"/> en
    /// <see langword="true"/> -- validado en <c>PublicarWorkflowVersionCommandHandler</c>, no acá (este
    /// tipo no conoce a sus hermanos).</summary>
    public bool EsInicial { get; private set; }

    /// <summary>Un estado final no tiene transiciones salientes; una instancia que lo alcanza queda
    /// <see cref="Instancias.WorkflowInstanceEstado.Finalizada"/>.</summary>
    public bool EsFinal { get; private set; }

    /// <summary>Si es <see langword="true"/>, entrar a este estado crea una <see cref="Instancias.WorkflowTask"/>
    /// (acción humana, "pasos condicionales"/"aprobación y rechazo" del Plan Maestro) y la instancia se
    /// detiene ahí hasta que la tarea se resuelva; si es <see langword="false"/>, el motor intenta avanzar
    /// automáticamente evaluando las <see cref="WorkflowTransition"/> salientes (útil para un estado de
    /// bifurcación puramente condicional, sin intervención humana).</summary>
    public bool RequiereTarea { get; private set; }

    /// <summary>Título por defecto de la tarea creada al entrar a este estado (solo aplica si
    /// <see cref="RequiereTarea"/>). <see langword="null"/> para un estado que no requiere tarea.</summary>
    public string? TituloTarea { get; private set; }

    /// <summary>Actor (usuario concreto, ver comentario de <see cref="Instancias.WorkflowTask.AsignadoAUserId"/>
    /// sobre por qué el primer alcance no soporta rol/cargo) al que se asigna la tarea creada al entrar a
    /// este estado. Obligatorio cuando <see cref="RequiereTarea"/> es <see langword="true"/>, validado en
    /// <c>PublicarWorkflowVersionCommandHandler</c>.</summary>
    public Guid? AsignadoPorDefectoUserId { get; private set; }

    /// <summary>Minutos de SLA por defecto para la tarea creada al entrar a este estado -- si se vence sin
    /// resolverse, <c>WorkflowEscalamientoJob</c> la reasigna a <see cref="EscalarAUserId"/> (ver
    /// <c>docs/guia-workflow.md</c>, sección "Timeout y SLA"). <see langword="null"/> = sin SLA (la tarea
    /// nunca se escala automáticamente).</summary>
    public int? SlaMinutos { get; private set; }

    /// <summary>Actor al que se reasigna automáticamente una tarea de este estado si vence su SLA --
    /// deliberadamente un único usuario fijo por estado (no una jerarquía dinámica de escalamiento vía
    /// Organization/cargo, fuera del primer alcance mínimo, ver <c>docs/guia-workflow.md</c>, sección
    /// "Pendientes"). Obligatorio cuando <see cref="SlaMinutos"/> no es nulo.</summary>
    public Guid? EscalarAUserId { get; private set; }

    public Guid TenantId { get; set; }

    public WorkflowState(
        Guid id, Guid workflowVersionId, string codigo, string nombre, bool esInicial, bool esFinal,
        bool requiereTarea, string? tituloTarea, Guid? asignadoPorDefectoUserId, int? slaMinutos,
        Guid? escalarAUserId)
        : base(id)
    {
        WorkflowVersionId = workflowVersionId;
        Codigo = codigo;
        Nombre = nombre;
        EsInicial = esInicial;
        EsFinal = esFinal;
        RequiereTarea = requiereTarea;
        TituloTarea = tituloTarea;
        AsignadoPorDefectoUserId = asignadoPorDefectoUserId;
        SlaMinutos = slaMinutos;
        EscalarAUserId = escalarAUserId;
    }

    private WorkflowState()
    {
    }
}
