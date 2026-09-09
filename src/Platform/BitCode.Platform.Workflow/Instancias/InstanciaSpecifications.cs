using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.Workflow.Instancias;

internal sealed class HistorialDeInstanciaSpecification : Specification<WorkflowHistorial>
{
    public HistorialDeInstanciaSpecification(Guid workflowInstanceId)
    {
        ApplyCriteria(h => h.WorkflowInstanceId == workflowInstanceId);
        ApplyOrderBy(h => h.FechaUtc);
    }
}

/// <summary>Tareas pendientes asignadas a un actor concreto -- la bandeja mínima de este módulo (Task
/// Inbox, Fase 6 módulo 7, construirá la experiencia rica; esta query es solo el contrato de lectura
/// mínimo que Workflow expone por sí mismo).</summary>
internal sealed class TareasPendientesDeActorSpecification : Specification<WorkflowTask>
{
    public TareasPendientesDeActorSpecification(Guid asignadoAUserId)
    {
        ApplyCriteria(t => t.AsignadoAUserId == asignadoAUserId && t.Estado == WorkflowTaskEstado.Pendiente);
        ApplyOrderBy(t => t.CreatedAtUtc);
    }
}

/// <summary>Tareas pendientes, no escaladas todavía, con SLA vencido -- el filtro que
/// <c>WorkflowEscalamientoJob</c> ejecuta en cada disparo.</summary>
internal sealed class TareasVencidasSinEscalarSpecification : Specification<WorkflowTask>
{
    public TareasVencidasSinEscalarSpecification(DateTime ahoraUtc)
    {
        ApplyCriteria(t =>
            t.Estado == WorkflowTaskEstado.Pendiente &&
            !t.Escalada &&
            t.SlaVencimientoUtc != null &&
            t.SlaVencimientoUtc < ahoraUtc);
    }
}
