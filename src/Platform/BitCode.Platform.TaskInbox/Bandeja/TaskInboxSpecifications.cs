using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.TaskInbox.Bandeja;

/// <summary>
/// Filtros de la bandeja propia de un actor (Fase 6, módulo 7: "filtros" del Plan Maestro). Igual que
/// <c>TareasPendientesDeActorSpecification</c> de Workflow, SIEMPRE filtra por el actor autenticado --
/// nunca recibe el <c>AsignadoAUserId</c> de otro usuario como parámetro de un cliente, para que nadie
/// pueda leer la bandeja ajena.
/// </summary>
/// <remarks>
/// Filtros honestamente NO soportados en este primer corte (ver <c>docs/guia-taskinbox.md</c>, "Qué
/// quedó completo y qué no"): por definición de workflow, por prioridad, por vencimiento de SLA y
/// texto libre en título. Los tres eventos de integración que este módulo consume
/// (<c>TareaAsignadaIntegrationEvent</c>/<c>TareaAprobadaIntegrationEvent</c>/
/// <c>TareaRechazadaIntegrationEvent</c>) no llevan esos datos -- solo identificadores
/// (<c>WorkflowTaskId</c>/<c>WorkflowInstanceId</c>/<c>AsignadoAUserId</c>/<c>ResueltaPorUserId</c>).
/// Enriquecerlos exigiría, o bien modificar el contrato de esos eventos (fuera de alcance: este módulo
/// no debe tocar Workflow), o bien que este read-model llamara sincrónicamente a la API pública de
/// Workflow para completar cada fila (acoplamiento síncrono entre bounded contexts que esta tarea
/// decidió NO introducir sin una necesidad de negocio concreta que lo justifique).
/// </remarks>
internal sealed class BandejaDeActorSpecification : Specification<TaskInboxItem>
{
    public BandejaDeActorSpecification(
        Guid asignadoAUserId, TaskInboxEstado? estado, Guid? workflowInstanceId, DateTime? desdeUtc, DateTime? hastaUtc)
    {
        ApplyCriteria(i =>
            i.AsignadoAUserId == asignadoAUserId &&
            (estado == null || i.Estado == estado) &&
            (workflowInstanceId == null || i.WorkflowInstanceId == workflowInstanceId) &&
            (desdeUtc == null || i.AsignadaAtUtc >= desdeUtc) &&
            (hastaUtc == null || i.AsignadaAtUtc <= hastaUtc));
        ApplyOrderByDescending(i => i.AsignadaAtUtc);
    }
}
