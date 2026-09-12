using System.Text.Json;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Instancias;

public enum WorkflowInstanceEstado
{
    EnCurso = 0,
    Finalizada = 1,
}

/// <summary>
/// Una ejecución concreta de una <see cref="Definiciones.WorkflowVersion"/> (Fase 6, módulo Workflow),
/// con su <see cref="EstadoActualId"/> (referencia a un <c>WorkflowState.Id</c> de esa misma versión) y su
/// diccionario de variables de contexto (<see cref="VariablesJson"/>, usado por
/// <c>WorkflowRuleEvaluator</c> para decidir qué <c>WorkflowTransition</c> aplica). Queda atada para
/// siempre a la versión con la que arrancó (<see cref="WorkflowVersionId"/>) -- publicar una versión nueva
/// del mismo <see cref="WorkflowDefinitionId"/> nunca reasigna instancias ya en curso, ver
/// <c>docs/guia-workflow.md</c>.
/// </summary>
public sealed class WorkflowInstance : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, IHasConcurrencyToken
{
    public Guid WorkflowDefinitionId { get; private set; }

    public Guid WorkflowVersionId { get; private set; }

    public Guid EstadoActualId { get; private set; }

    public WorkflowInstanceEstado Estado { get; private set; } = WorkflowInstanceEstado.EnCurso;

    /// <summary>Diccionario de variables de contexto serializado como JSON (<c>Dictionary&lt;string,
    /// string&gt;</c>) -- se guarda como texto plano (no un tipo de columna JSON nativo de SQL Server)
    /// para mantener el mapeo de EF Core simple; ningún handler de este módulo consulta dentro de este
    /// JSON vía SQL (nunca <c>IQueryable</c> expuesto, regla dura 5) -- siempre se lee la instancia
    /// completa y se deserializa en memoria.</summary>
    public string VariablesJson { get; private set; } = "{}";

    public Guid IniciadoPorUserId { get; private set; }

    public DateTime? FinalizadaAtUtc { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    /// <summary>Token de concurrencia optimista (F1-08, <c>IHasConcurrencyToken</c>) -- ver el mismo
    /// razonamiento en <see cref="WorkflowTask.RowVersion"/>: <see cref="AvanzarA"/> puede ser invocado
    /// por dos transiciones concurrentes resueltas sobre el mismo estado (p. ej. dos tareas paralelas
    /// resolviéndose casi al mismo tiempo) y esta entidad no tenía ninguna protección contra pisar el
    /// avance de la otra.</summary>
    public byte[] RowVersion { get; set; } = [];

    public WorkflowInstance(
        Guid id, Guid workflowDefinitionId, Guid workflowVersionId, Guid estadoInicialId,
        Guid iniciadoPorUserId, IReadOnlyDictionary<string, string> variables)
        : base(id)
    {
        WorkflowDefinitionId = workflowDefinitionId;
        WorkflowVersionId = workflowVersionId;
        EstadoActualId = estadoInicialId;
        IniciadoPorUserId = iniciadoPorUserId;
        VariablesJson = JsonSerializer.Serialize(variables);

        RaiseDomainEvent(new WorkflowInstanciaIniciadaIntegrationEvent(Id, WorkflowDefinitionId, WorkflowVersionId, IniciadoPorUserId));
    }

    private WorkflowInstance()
    {
    }

    public Dictionary<string, string> ObtenerVariables() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(VariablesJson) ?? [];

    /// <summary>Mueve la instancia a un estado nuevo del mismo grafo -- llamado por el motor de avance
    /// (<c>IniciarInstanciaCommandHandler</c>/<c>ResolverTareaCommandHandler</c>) después de resolver la
    /// <c>WorkflowTransition</c> aplicable. Si <paramref name="esFinal"/>, la instancia queda
    /// <see cref="WorkflowInstanceEstado.Finalizada"/> de forma irreversible.</summary>
    public void AvanzarA(Guid nuevoEstadoId, bool esFinal)
    {
        EstadoActualId = nuevoEstadoId;

        if (esFinal)
        {
            Estado = WorkflowInstanceEstado.Finalizada;
            FinalizadaAtUtc = DateTime.UtcNow;
            RaiseDomainEvent(new WorkflowInstanciaFinalizadaIntegrationEvent(Id, WorkflowDefinitionId, nuevoEstadoId));
        }
    }
}
