/**
 * Estado de una `WorkflowTask` -- valores verificados contra
 * `BitCode.Framework.Platform.Workflow.Instancias.WorkflowTaskEstado`
 * (`src/Platform/BitCode.Platform.Workflow/Instancias/WorkflowTask.cs`).
 */
export enum WorkflowTaskEstado {
  Pendiente = 0,
  Resuelta = 1,
}

const WORKFLOW_TASK_ESTADO_LABELS: Readonly<Record<WorkflowTaskEstado, string>> = {
  [WorkflowTaskEstado.Pendiente]: 'Pendiente',
  [WorkflowTaskEstado.Resuelta]: 'Resuelta',
};

export function workflowTaskEstadoLabel(estado: WorkflowTaskEstado): string {
  return WORKFLOW_TASK_ESTADO_LABELS[estado] ?? `Desconocido (${estado})`;
}

/**
 * Tarea humana de Workflow vista desde el cliente -- shape verificada 1:1 contra `WorkflowTaskResponse`
 * (`src/Platform/BitCode.Platform.Workflow/Instancias/Responses.cs`), tal como la devuelve
 * `GET /api/v1/workflows/tareas/{id}` y `GET /api/v1/workflows/tareas/pendientes` (paginado, SOLO las del
 * actor autenticado -- ver `docs/guia-workflow.md`).
 */
export interface WorkflowTask {
  readonly id: string;
  readonly workflowInstanceId: string;
  readonly workflowStateId: string;
  readonly titulo: string;
  readonly asignadoAUserId: string;
  readonly estado: WorkflowTaskEstado;
  readonly accionResuelta?: string | null;
  readonly slaVencimientoUtc?: string | null;
  readonly escalada: boolean;
}
