/**
 * Estado de una `WorkflowInstance` -- valores verificados contra
 * `BitCode.Framework.Platform.Workflow.Instancias.WorkflowInstanceEstado`
 * (`src/Platform/BitCode.Platform.Workflow/Instancias/WorkflowInstance.cs`).
 */
export enum WorkflowInstanceEstado {
  EnCurso = 0,
  Finalizada = 1,
}

const WORKFLOW_INSTANCE_ESTADO_LABELS: Readonly<Record<WorkflowInstanceEstado, string>> = {
  [WorkflowInstanceEstado.EnCurso]: 'En curso',
  [WorkflowInstanceEstado.Finalizada]: 'Finalizada',
};

export function workflowInstanceEstadoLabel(estado: WorkflowInstanceEstado): string {
  return WORKFLOW_INSTANCE_ESTADO_LABELS[estado] ?? `Desconocido (${estado})`;
}

/**
 * Instancia concreta de un workflow -- shape verificada 1:1 contra `WorkflowInstanceResponse`
 * (`src/Platform/BitCode.Platform.Workflow/Instancias/Responses.cs`), tal como la devuelve
 * `GET /api/v1/workflows/instancias/{id}` (ver `docs/guia-workflow.md`).
 */
export interface WorkflowInstance {
  readonly id: string;
  readonly workflowDefinitionId: string;
  readonly workflowVersionId: string;
  /** Id de la `WorkflowState` actual -- para mostrar un nombre legible hace falta resolverlo contra el
   * grafo de la versión (`WorkflowVersion.estados`, ver `workflow-version.model.ts` y
   * `resolveCurrentWorkflowState`). La instancia por sí sola NO expone el nombre del estado. */
  readonly estadoActualId: string;
  readonly estado: WorkflowInstanceEstado;
  readonly variables: Readonly<Record<string, string>>;
  readonly finalizadaAtUtc?: string | null;
}

/**
 * Entrada de historial append-only -- shape verificada 1:1 contra `WorkflowHistorialResponse`
 * (`src/Platform/BitCode.Platform.Workflow/Instancias/Responses.cs`), tal como la devuelve
 * `GET /api/v1/workflows/instancias/{id}/historial` (lista completa, SIN paginar -- ver
 * `docs/guia-workflow.md`, "volumen acotado por el propio grafo").
 */
export interface WorkflowHistorialEntry {
  readonly id: string;
  readonly fechaUtc: string;
  readonly tipoEvento: string;
  readonly detalle: string;
  readonly actorUserId?: string | null;
}
