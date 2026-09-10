import { WorkflowInstance } from './workflow-instance.model';

/**
 * Estado de una `WorkflowVersion` -- valores verificados contra
 * `BitCode.Framework.Platform.Workflow.Definiciones.WorkflowVersionEstado`
 * (`src/Platform/BitCode.Platform.Workflow/Definiciones/WorkflowVersion.cs`).
 */
export enum WorkflowVersionEstado {
  Borrador = 0,
  Publicada = 1,
}

/**
 * Nodo del grafo (`WorkflowState`) -- shape verificada 1:1 contra `WorkflowStateResponse`
 * (`src/Platform/BitCode.Platform.Workflow/Definiciones/Responses.cs`).
 */
export interface WorkflowState {
  readonly id: string;
  readonly codigo: string;
  readonly nombre: string;
  readonly esInicial: boolean;
  readonly esFinal: boolean;
  readonly requiereTarea: boolean;
  readonly tituloTarea?: string | null;
  readonly asignadoPorDefectoUserId?: string | null;
  readonly slaMinutos?: number | null;
  readonly escalarAUserId?: string | null;
}

/**
 * Arista del grafo (`WorkflowTransition`) -- shape verificada 1:1 contra `WorkflowTransitionResponse`
 * (`src/Platform/BitCode.Platform.Workflow/Definiciones/Responses.cs`). `accion` es el valor EXACTO que
 * hay que enviar en `ResolverTareaCommand`/`POST /api/v1/workflows/tareas/{id}/resolver` (`Accion`, texto
 * libre definido por quien creó la versión -- NO es un enum cerrado del backend, ver
 * `docs/guia-frontend-workflow.md`).
 */
export interface WorkflowTransition {
  readonly id: string;
  readonly desdeEstadoId: string;
  readonly haciaEstadoId: string;
  readonly accion: string;
  readonly reglaExpresion?: string | null;
  readonly orden: number;
}

/**
 * Grafo completo de una versión de workflow -- shape verificada 1:1 contra `WorkflowVersionResponse`
 * (`src/Platform/BitCode.Platform.Workflow/Definiciones/Responses.cs`), tal como la devuelve
 * `GET /api/v1/workflows/versiones/{id}`.
 */
export interface WorkflowVersion {
  readonly id: string;
  readonly workflowDefinitionId: string;
  readonly numero: number;
  readonly estado: WorkflowVersionEstado;
  readonly publicadaAtUtc?: string | null;
  readonly estados: readonly WorkflowState[];
  readonly transiciones: readonly WorkflowTransition[];
}

/**
 * Resuelve el `WorkflowState` actual de una instancia contra el grafo de SU versión (`instance.
 * workflowVersionId` debe coincidir con `version.id` -- responsabilidad del llamador, ver
 * `WorkflowInstanceStatusService`). Devuelve `null` si no se encuentra (grafo inconsistente o instancia de
 * otra versión) -- nunca lanza, para que la UI pueda degradar mostrando sólo el id crudo.
 */
export function resolveCurrentWorkflowState(
  version: WorkflowVersion,
  instance: Pick<WorkflowInstance, 'estadoActualId'>,
): WorkflowState | null {
  return version.estados.find((state) => state.id === instance.estadoActualId) ?? null;
}

/**
 * Transiciones salientes del estado actual de una instancia, ordenadas por `orden` -- son las ÚNICAS
 * acciones (`WorkflowTransition.accion`) que `ResolverTareaCommand` aceptará realmente para una tarea de
 * esa instancia (evaluando además `reglaExpresion` contra las variables, ver `docs/guia-workflow.md`,
 * "Reglas"). Reemplaza cualquier lista de acciones hardcodeada ("Aprobar"/"Rechazar") por las transiciones
 * REALES definidas en el grafo publicado -- ver `docs/guia-frontend-workflow.md` para por qué esto es
 * preferible a asumir nombres de acción fijos.
 */
export function resolveAvailableTransitions(
  version: WorkflowVersion,
  instance: Pick<WorkflowInstance, 'estadoActualId'>,
): readonly WorkflowTransition[] {
  return version.transiciones
    .filter((transition) => transition.desdeEstadoId === instance.estadoActualId)
    .slice()
    .sort((a, b) => a.orden - b.orden);
}
