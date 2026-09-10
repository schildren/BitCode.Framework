import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { WorkflowTask } from '../models/workflow-task.model';

/** Shape REAL del body de `POST /api/v1/workflows/tareas/{id}/resolver` -- verificada 1:1 contra
 * `ResolverTareaRequest` (`internal sealed record`, `WorkflowEndpointRouteBuilderExtensions.cs`). */
interface ResolverTareaRequestDto {
  readonly accion: string;
  readonly comentario?: string | null;
}

/** Shape REAL del body de `POST /api/v1/workflows/tareas/{id}/delegar` -- verificada 1:1 contra
 * `DelegarTareaRequest` (`WorkflowEndpointRouteBuilderExtensions.cs`). */
interface DelegarTareaRequestDto {
  readonly nuevoAsignadoUserId: string;
}

/**
 * Acciones REALES sobre una `WorkflowTask` (F7-09) -- envuelve exactamente los dos endpoints de mutación
 * que expone Workflow para una tarea (ver `docs/guia-workflow.md`, tabla de endpoints). Task Inbox
 * DELIBERADAMENTE no expone ningún endpoint de resolución/delegación (ver `docs/guia-taskinbox.md`,
 * "El límite de bounded context") -- toda acción real viaja siempre contra `/api/v1/workflows/tareas/...`,
 * incluso cuando el `id` provino de una fila de `TaskInboxItem` (son el mismo id, ver
 * `TaskInboxItem.id`).
 *
 * Este servicio NO mapea errores a `BitcodeUiError` -- deja el `HttpErrorResponse`/`BitcodeHttpError`
 * (si `bitcodeErrorInterceptor` de F7-06 está registrado) tal cual para que el componente consumidor
 * (`WorkflowTaskActions`, o cualquier otro) decida cómo mostrarlo, mismo criterio que
 * `BitcodeGridDataSource<T>.loadPage`/`BitcodeFormSubmitHandler<T>.submit`.
 */
@Injectable({ providedIn: 'root' })
export class WorkflowTaskActionsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/workflows/tareas';

  obtenerTarea(id: string): Observable<WorkflowTask> {
    return this.http.get<WorkflowTask>(`${this.baseUrl}/${id}`);
  }

  /**
   * Resuelve la tarea con una acción concreta -- `accion` DEBE ser el valor exacto de una
   * `WorkflowTransition.accion` saliente del estado actual de la instancia (ver
   * `resolveAvailableTransitions`, `workflow-version.model.ts`); el backend no valida el valor contra un
   * enum cerrado, pero lo rechaza con 409 si no corresponde a ninguna transición válida desde el estado
   * actual (`IWorkflowEngine.AvanzarAsync`). 403 si el actor autenticado no es el asignado actual
   * (`WorkflowTask.AsignadoAUserId`, ver `docs/guia-workflow.md`, "RBAC y ownership").
   */
  resolver(id: string, accion: string, comentario?: string): Observable<void> {
    const body: ResolverTareaRequestDto = { accion, comentario: comentario ?? null };
    return this.http.post<void>(`${this.baseUrl}/${id}/resolver`, body);
  }

  /** Reasigna la tarea a otro usuario sin resolverla. Mismo control de ownership 403 que `resolver`. */
  delegar(id: string, nuevoAsignadoUserId: string): Observable<void> {
    const body: DelegarTareaRequestDto = { nuevoAsignadoUserId };
    return this.http.post<void>(`${this.baseUrl}/${id}/delegar`, body);
  }
}
