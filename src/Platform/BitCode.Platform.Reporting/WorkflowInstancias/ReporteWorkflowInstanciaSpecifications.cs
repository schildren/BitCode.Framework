using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.Reporting.WorkflowInstancias;

/// <summary>
/// Filtros del listado/exportación de <see cref="ReporteWorkflowInstancia"/> (Fase 6, módulo 11: "read
/// models" + "exportación" del Plan Maestro). Sin ownership por actor -- a diferencia de
/// <c>BandejaDeActorSpecification</c> (Task Inbox), este read-model es agregado/operacional (mismo
/// criterio honesto que Integration Hub, Fase 6 módulo 9, aplicó a sus consultas de solicitudes): cualquier
/// actor con el permiso RBAC correspondiente ve todas las instancias del tenant, no solo las propias. Ver
/// "RBAC y ABAC" en <c>docs/guia-reporting.md</c>.
/// </summary>
internal sealed class ReporteWorkflowInstanciaFiltroSpecification : Specification<ReporteWorkflowInstancia>
{
    public ReporteWorkflowInstanciaFiltroSpecification(
        Guid? workflowDefinitionId, ReporteWorkflowInstanciaEstado? estado, DateTime? desdeUtc, DateTime? hastaUtc)
    {
        ApplyCriteria(r =>
            (workflowDefinitionId == null || r.WorkflowDefinitionId == workflowDefinitionId) &&
            (estado == null || r.Estado == estado) &&
            (desdeUtc == null || (r.IniciadaAtUtc != null && r.IniciadaAtUtc >= desdeUtc)) &&
            (hastaUtc == null || (r.IniciadaAtUtc != null && r.IniciadaAtUtc <= hastaUtc)));
        ApplyOrderByDescending(r => r.IniciadaAtUtc!);
    }
}
