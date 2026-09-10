import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { Subscription, forkJoin } from 'rxjs';
import { BitcodeErrorExperienceService, BitcodeHttpError, BitcodeUiError } from '@bitcode/core';
import { WorkflowHistorialEntry, WorkflowInstance, workflowInstanceEstadoLabel } from '../models/workflow-instance.model';
import {
  WorkflowTransition,
  WorkflowVersion,
  resolveAvailableTransitions,
  resolveCurrentWorkflowState,
} from '../models/workflow-version.model';
import { WorkflowInstanceStatusService } from './workflow-instance-status.service';

export type WorkflowInstanceStatusLoadStatus = 'idle' | 'loading' | 'loaded' | 'error';

/**
 * Visualización de "estado actual + historial" de UNA instancia de Workflow (F7-09) -- explícitamente NO
 * un diagramador visual del grafo (fuera de alcance, ver Plan Maestro Fase 6 "Épica de Workflow": "diseñador
 * BPMN completo" queda fuera de alcance del propio backend, así que con más razón de esta UI). Combina tres
 * llamadas de sólo lectura reales (`WorkflowInstanceStatusService`):
 *
 * 1. `GET /api/v1/workflows/instancias/{id}` -- la instancia en sí.
 * 2. `GET /api/v1/workflows/versiones/{instance.workflowVersionId}` -- el grafo, para resolver el NOMBRE
 *    del estado actual (`resolveCurrentWorkflowState`) y las transiciones salientes disponibles
 *    (`resolveAvailableTransitions`) -- la instancia por sí sola sólo expone `estadoActualId` (un Guid).
 * 3. `GET /api/v1/workflows/instancias/{id}/historial` -- lista completa (sin paginar, ver
 *    `WorkflowInstanceStatusService`), ordenada acá por `fechaUtc` ascendente antes de mostrarse.
 *
 * Igual criterio de error que `BitcodeGrid`/`BitcodeDynamicForm` (F7-06/F7-07/F7-08): nunca un mensaje
 * inventado, siempre `BitcodeUiError` vía `BitcodeErrorExperienceService`/`BitcodeHttpError.uiError`.
 */
@Component({
  selector: 'lib-workflow-instance-status',
  imports: [],
  templateUrl: './workflow-instance-status.html',
  styleUrl: './workflow-instance-status.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkflowInstanceStatus {
  private readonly service = inject(WorkflowInstanceStatusService);
  private readonly errorExperience = inject(BitcodeErrorExperienceService);

  readonly instanceId = input.required<string>();

  readonly status = signal<WorkflowInstanceStatusLoadStatus>('idle');
  readonly instance = signal<WorkflowInstance | null>(null);
  readonly version = signal<WorkflowVersion | null>(null);
  readonly historial = signal<readonly WorkflowHistorialEntry[]>([]);
  readonly error = signal<BitcodeUiError | null>(null);

  readonly estadoInstanciaLabel = computed(() => {
    const instance = this.instance();
    return instance ? workflowInstanceEstadoLabel(instance.estado) : '';
  });

  readonly estadoActualNombre = computed(() => {
    const instance = this.instance();
    const version = this.version();
    if (!instance || !version) {
      return null;
    }
    return resolveCurrentWorkflowState(version, instance)?.nombre ?? null;
  });

  readonly transicionesDisponibles = computed<readonly WorkflowTransition[]>(() => {
    const instance = this.instance();
    const version = this.version();
    if (!instance || !version) {
      return [];
    }
    return resolveAvailableTransitions(version, instance);
  });

  readonly historialOrdenado = computed(() =>
    [...this.historial()].sort((a, b) => Date.parse(a.fechaUtc) - Date.parse(b.fechaUtc)),
  );

  private currentSubscription?: Subscription;

  constructor() {
    effect(() => {
      const instanceId = this.instanceId();
      this.load(instanceId);
    });
  }

  reload(): void {
    this.load(this.instanceId());
  }

  private load(instanceId: string): void {
    this.currentSubscription?.unsubscribe();
    this.status.set('loading');
    this.error.set(null);

    this.currentSubscription = this.service.obtenerInstancia(instanceId).subscribe({
      next: (instance) => {
        this.instance.set(instance);
        this.loadVersionAndHistorial(instanceId, instance.workflowVersionId);
      },
      error: (rawError: unknown) => this.fail(rawError),
    });
  }

  private loadVersionAndHistorial(instanceId: string, versionId: string): void {
    this.currentSubscription = forkJoin({
      version: this.service.obtenerVersion(versionId),
      historial: this.service.obtenerHistorial(instanceId),
    }).subscribe({
      next: ({ version, historial }) => {
        this.version.set(version);
        this.historial.set(historial);
        this.status.set('loaded');
      },
      error: (rawError: unknown) => this.fail(rawError),
    });
  }

  private fail(rawError: unknown): void {
    this.error.set(this.toUiError(rawError));
    this.status.set('error');
  }

  private toUiError(error: unknown): BitcodeUiError {
    if (error instanceof BitcodeHttpError) {
      return error.uiError;
    }
    return this.errorExperience.fromHttpError(error);
  }
}
