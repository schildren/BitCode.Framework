import type { components } from './generated/sample-workflow-api.v1';
import type { WorkflowInstance, WorkflowHistorialEntry } from '../models/workflow-instance.model';
import type { WorkflowTask } from '../models/workflow-task.model';
import type { WorkflowState, WorkflowTransition, WorkflowVersion } from '../models/workflow-version.model';

/**
 * Verificación en tiempo de COMPILACIÓN de que los modelos escritos a mano en `models/*.model.ts`
 * mantienen, campo por campo, la misma forma que el contrato OpenAPI real generado por
 * `Sample.Workflow.Api` (ver `docs/guia-contratos-frontend.md` y
 * `documents/src/lib/contracts/contract-consistency.ts` para la justificación completa de por qué NO se
 * re-exportan los tipos generados directamente: el generador nativo de OpenAPI de .NET 10 describe los
 * enteros como `number | string`, una particularidad real del schema que degradaría la superficie
 * pública de este paquete sin aportar seguridad real).
 *
 * `TaskInboxItem` (`models/task-inbox-item.model.ts`) queda FUERA de esta verificación a propósito: su
 * contrato real es `Sample.TaskInbox.Api`, que `samples/OpenApiExport` todavía no exporta (ver
 * `docs/guia-contratos-frontend.md`, sección "Cobertura actual y pendientes").
 */
type Expect<T extends true> = T;

type KeysMatch<Hand, Generated> = [keyof Hand] extends [keyof Generated]
  ? [keyof Generated] extends [keyof Hand]
    ? true
    : false
  : false;

// eslint-disable-next-line @typescript-eslint/no-unused-vars
type _WorkflowInstanceTieneLasMismasClavesQueElContratoGenerado = Expect<
  KeysMatch<WorkflowInstance, components['schemas']['WorkflowInstanceResponse']>
>;

// eslint-disable-next-line @typescript-eslint/no-unused-vars
type _WorkflowHistorialEntryTieneLasMismasClavesQueElContratoGenerado = Expect<
  KeysMatch<WorkflowHistorialEntry, components['schemas']['WorkflowHistorialResponse']>
>;

// eslint-disable-next-line @typescript-eslint/no-unused-vars
type _WorkflowTaskTieneLasMismasClavesQueElContratoGenerado = Expect<
  KeysMatch<WorkflowTask, components['schemas']['WorkflowTaskResponse']>
>;

// eslint-disable-next-line @typescript-eslint/no-unused-vars
type _WorkflowStateTieneLasMismasClavesQueElContratoGenerado = Expect<
  KeysMatch<WorkflowState, components['schemas']['WorkflowStateResponse']>
>;

// eslint-disable-next-line @typescript-eslint/no-unused-vars
type _WorkflowTransitionTieneLasMismasClavesQueElContratoGenerado = Expect<
  KeysMatch<WorkflowTransition, components['schemas']['WorkflowTransitionResponse']>
>;

// eslint-disable-next-line @typescript-eslint/no-unused-vars
type _WorkflowVersionTieneLasMismasClavesQueElContratoGenerado = Expect<
  KeysMatch<WorkflowVersion, components['schemas']['WorkflowVersionResponse']>
>;
