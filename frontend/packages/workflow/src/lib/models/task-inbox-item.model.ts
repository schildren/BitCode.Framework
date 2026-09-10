/**
 * Estado de un `TaskInboxItem` -- reflejo del estado de la `WorkflowTask` de origen tal como lo reporta
 * Task Inbox (Fase 6, módulo 7), NUNCA calculado ni decidido por este paquete de UI. Valores verificados
 * contra `BitCode.Framework.Platform.TaskInbox.Bandeja.TaskInboxEstado`
 * (`src/Platform/BitCode.Platform.TaskInbox/Bandeja/TaskInboxItem.cs`) -- el backend serializa el enum
 * como su valor numérico (sin `JsonStringEnumConverter` registrado en ningún host de referencia), así que
 * este enum de TypeScript usa EXACTAMENTE los mismos valores, no nombres de cadena inventados.
 */
export enum TaskInboxItemEstado {
  Pendiente = 0,
  Aprobada = 1,
  Rechazada = 2,
}

const TASK_INBOX_ITEM_ESTADO_LABELS: Readonly<Record<TaskInboxItemEstado, string>> = {
  [TaskInboxItemEstado.Pendiente]: 'Pendiente',
  [TaskInboxItemEstado.Aprobada]: 'Aprobada',
  [TaskInboxItemEstado.Rechazada]: 'Rechazada',
};

export function taskInboxItemEstadoLabel(estado: TaskInboxItemEstado): string {
  return TASK_INBOX_ITEM_ESTADO_LABELS[estado] ?? `Desconocido (${estado})`;
}

/**
 * Read-model de UNA tarea humana de Workflow visto desde el cliente -- shape verificada 1:1 contra
 * `TaskInboxItemResponse` (`src/Platform/BitCode.Platform.TaskInbox/Bandeja/Responses.cs`), tal como lo
 * devuelve `GET /api/v1/taskinbox/bandeja` (paginado) y `GET /api/v1/taskinbox/bandeja/{id}` (ver
 * `docs/guia-taskinbox.md`).
 *
 * **Honestidad deliberada, no un olvido:** este DTO NO tiene `prioridad` ni fecha de vencimiento de SLA
 * propia -- los tres eventos de integración de Workflow que Task Inbox consume
 * (`TareaAsignadaIntegrationEvent`/`TareaAprobadaIntegrationEvent`/`TareaRechazadaIntegrationEvent`) no
 * transportan esos datos (ver `docs/guia-taskinbox.md`, sección "Filtros — qué se soporta y qué no").
 * `WorkflowTask.slaVencimientoUtc` SÍ existe, pero sólo en el módulo Workflow
 * (`GET /api/v1/workflows/tareas/{id}`, ver `workflow-task.model.ts`) -- inventar un campo de prioridad o
 * SLA acá hubiera sido un contrato no verificable contra el backend real.
 */
export interface TaskInboxItem {
  /** Igual al `WorkflowTaskId` de origen -- clave natural entre bounded contexts (ver `guia-taskinbox.md`). */
  readonly id: string;
  readonly workflowInstanceId: string;
  readonly asignadoAUserId: string;
  readonly estado: TaskInboxItemEstado;
  readonly asignadaAtUtc: string;
  readonly resueltaPorUserId?: string | null;
  readonly resueltaAtUtc?: string | null;
  readonly leidoAtUtc?: string | null;
}
