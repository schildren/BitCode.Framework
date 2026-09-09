using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

/// <summary>Especificaciones reutilizadas por más de un handler de <c>Definiciones</c> -- agrupadas en un
/// único archivo (regla dura 5, nunca <c>IQueryable</c> expuesto: toda consulta pasa por una
/// <see cref="Specification{T}"/>), mismo criterio que <c>CatalogoSpecifications</c>.</summary>
internal sealed class WorkflowDefinitionPorCodigoSpecification : Specification<WorkflowDefinition>
{
    public WorkflowDefinitionPorCodigoSpecification(string codigo) => ApplyCriteria(d => d.Codigo == codigo);
}

internal sealed class TodasLasWorkflowDefinicionesOrdenadasPorCodigoSpecification : Specification<WorkflowDefinition>
{
    public TodasLasWorkflowDefinicionesOrdenadasPorCodigoSpecification() => ApplyOrderBy(d => d.Codigo);
}

internal sealed class VersionesDeWorkflowSpecification : Specification<WorkflowVersion>
{
    public VersionesDeWorkflowSpecification(Guid workflowDefinitionId) => ApplyCriteria(v => v.WorkflowDefinitionId == workflowDefinitionId);
}

/// <summary>La última versión publicada de un workflow -- la que <c>IniciarInstanciaCommandHandler</c>
/// usa para crear una instancia nueva a partir del <c>WorkflowDefinitionId</c>.</summary>
internal sealed class UltimaVersionPublicadaSpecification : Specification<WorkflowVersion>
{
    public UltimaVersionPublicadaSpecification(Guid workflowDefinitionId)
    {
        ApplyCriteria(v => v.WorkflowDefinitionId == workflowDefinitionId && v.Estado == WorkflowVersionEstado.Publicada);
        ApplyOrderByDescending(v => v.Numero);
    }
}

internal sealed class EstadosDeVersionSpecification : Specification<WorkflowState>
{
    public EstadosDeVersionSpecification(Guid workflowVersionId) => ApplyCriteria(s => s.WorkflowVersionId == workflowVersionId);
}

internal sealed class TransicionesDeVersionSpecification : Specification<WorkflowTransition>
{
    public TransicionesDeVersionSpecification(Guid workflowVersionId)
    {
        ApplyCriteria(t => t.WorkflowVersionId == workflowVersionId);
        ApplyOrderBy(t => t.Orden);
    }
}

/// <summary>Transiciones salientes de un estado concreto, en orden de evaluación -- usado por el motor de
/// avance (<c>WorkflowEngine</c>) tanto al iniciar una instancia como al resolver una tarea.</summary>
internal sealed class TransicionesDesdeEstadoSpecification : Specification<WorkflowTransition>
{
    public TransicionesDesdeEstadoSpecification(Guid desdeEstadoId)
    {
        ApplyCriteria(t => t.DesdeEstadoId == desdeEstadoId);
        ApplyOrderBy(t => t.Orden);
    }
}
