import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { BitcodeDynamicForm, BitcodeFormFieldConfig, BitcodeFormSubmitHandler } from '@bitcode/forms';
import { BitcodeErrorExperienceService, BitcodeHttpError, BitcodeUiError } from '@bitcode/core';
import { WorkflowTaskActionsService } from './workflow-task-actions.service';
import { WorkflowTransition } from '../models/workflow-version.model';

/** Una acción concreta de resolución (`WorkflowTransition.accion`) presentada como botón. */
export interface WorkflowTaskActionOption {
  readonly accion: string;
  readonly label: string;
}

/**
 * Convención de acciones por defecto -- observada en `WorkflowEndpointsIntegrationTests`
 * (`samples/Sample.Workflow.Api.Tests/.../WorkflowEndpointsIntegrationTests.cs`), NO una garantía de
 * contrato del backend: `WorkflowTransition.accion` es texto libre definido por quien crea la
 * `WorkflowVersion`, así que un workflow real puede usar cualquier otro nombre. Se usa como fallback
 * únicamente cuando el consumidor no tiene a mano las transiciones reales del grafo -- ver
 * `workflowTaskActionOptionsFromTransitions` y `docs/guia-frontend-workflow.md`.
 */
export const DEFAULT_WORKFLOW_TASK_ACTIONS: readonly WorkflowTaskActionOption[] = [
  { accion: 'Aprobar', label: 'Aprobar' },
  { accion: 'Rechazar', label: 'Rechazar' },
];

/** Deriva las acciones disponibles de las transiciones REALES salientes del estado actual de una
 * instancia (`resolveAvailableTransitions`, `workflow-version.model.ts`) -- preferible siempre que el
 * consumidor ya haya resuelto el grafo (p. ej. vía `WorkflowInstanceStatus`/`WorkflowInstanceStatusService`
 * de este mismo paquete) en vez de asumir `DEFAULT_WORKFLOW_TASK_ACTIONS`. */
export function workflowTaskActionOptionsFromTransitions(
  transitions: readonly WorkflowTransition[],
): WorkflowTaskActionOption[] {
  return transitions.map((transition) => ({ accion: transition.accion, label: transition.accion }));
}

interface DelegarFormValue {
  readonly nuevoAsignadoUserId: string;
}

/**
 * Acciones sobre UNA tarea de Workflow (F7-09): aprobar/resolver con una acción concreta y reasignar
 * ("delegar"). Envuelve `WorkflowTaskActionsService` (`POST /api/v1/workflows/tareas/{id}/resolver`
 * y `.../delegar`, ver esa clase para el detalle de contrato) -- NUNCA los endpoints de Task Inbox, que
 * deliberadamente no exponen ninguna mutación de resolución (ver `docs/guia-taskinbox.md`).
 *
 * **Refresco, no actualización optimista (decisión documentada):** este componente NO muta ninguna fila de
 * `TaskInboxItem` localmente tras un éxito -- sólo emite `resolved`/`delegated`. Task Inbox es un
 * read-model asíncrono (`docs/guia-taskinbox.md`, "Límites conocidos" -- latencia de sincronización vía
 * Outbox → relay → broker → consumidor → Inbox); una actualización optimista de la fila de la bandeja
 * mostraría un estado que el backend real de Task Inbox puede tardar en reflejar, lo cual sería más
 * engañoso que simplemente recargar la página actual del grid (que puede, honestamente, seguir mostrando
 * el estado previo por una ventana corta -- el consumidor decide cuándo/si llamar `BitcodeGrid.reload()`
 * tras `resolved`/`delegated`, ver `docs/guia-frontend-workflow.md`).
 */
@Component({
  selector: 'lib-workflow-task-actions',
  imports: [ReactiveFormsModule, BitcodeDynamicForm],
  templateUrl: './workflow-task-actions.html',
  styleUrl: './workflow-task-actions.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkflowTaskActions {
  private readonly service = inject(WorkflowTaskActionsService);
  private readonly errorExperience = inject(BitcodeErrorExperienceService);

  readonly taskId = input.required<string>();
  readonly actions = input<readonly WorkflowTaskActionOption[]>(DEFAULT_WORKFLOW_TASK_ACTIONS);

  /** Emitido cuando `resolver(...)` tuvo éxito -- el consumidor decide si/cuándo refrescar cualquier grid
   * o vista que dependa de esta tarea (ver nota de "Refresco, no actualización optimista" arriba). */
  readonly resolved = output<void>();
  /** Emitido cuando `delegar(...)` tuvo éxito -- mismo criterio que `resolved`. */
  readonly delegated = output<void>();

  readonly comentario = new FormControl<string>('', { nonNullable: true });

  readonly status = signal<'idle' | 'resolving' | 'error'>('idle');
  readonly error = signal<BitcodeUiError | null>(null);

  readonly delegarFields: readonly BitcodeFormFieldConfig[] = [
    {
      name: 'nuevoAsignadoUserId',
      label: 'Reasignar a (Id de usuario)',
      placeholder: 'Guid del nuevo asignado',
      validators: { required: true },
      hint: 'Debe ser el Id (Guid) del usuario al que se reasigna la tarea.',
    },
  ];
  readonly delegarForm = new FormGroup({
    nuevoAsignadoUserId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  readonly delegarSubmitHandler: BitcodeFormSubmitHandler<DelegarFormValue> = {
    submit: (value) => this.service.delegar(this.taskId(), value.nuevoAsignadoUserId),
  };

  resolverAccion(accion: string): void {
    if (this.status() === 'resolving') {
      return;
    }
    this.status.set('resolving');
    this.error.set(null);

    const comentario = this.comentario.value.trim();
    this.service.resolver(this.taskId(), accion, comentario.length > 0 ? comentario : undefined).subscribe({
      next: () => {
        this.status.set('idle');
        this.comentario.reset('');
        this.resolved.emit();
      },
      error: (rawError: unknown) => {
        this.status.set('error');
        this.error.set(this.toUiError(rawError));
      },
    });
  }

  onDelegated(): void {
    this.delegarForm.reset({ nuevoAsignadoUserId: '' });
    this.delegated.emit();
  }

  private toUiError(error: unknown): BitcodeUiError {
    if (error instanceof BitcodeHttpError) {
      return error.uiError;
    }
    return this.errorExperience.fromHttpError(error);
  }
}
