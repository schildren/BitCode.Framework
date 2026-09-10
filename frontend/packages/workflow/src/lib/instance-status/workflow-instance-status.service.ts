import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { WorkflowHistorialEntry, WorkflowInstance } from '../models/workflow-instance.model';
import { WorkflowVersion } from '../models/workflow-version.model';

/**
 * Consultas REALES de sólo lectura sobre instancias de Workflow (F7-09) -- envuelve
 * `GET /api/v1/workflows/instancias/{id}`, `GET /api/v1/workflows/instancias/{id}/historial` y
 * `GET /api/v1/workflows/versiones/{id}` (esta última para resolver el nombre del estado actual y las
 * transiciones salientes, ver `workflow-version.model.ts`) -- shapes verificadas 1:1 contra
 * `WorkflowInstanceResponse`/`WorkflowHistorialResponse`/`WorkflowVersionResponse`
 * (`src/Platform/BitCode.Platform.Workflow/{Instancias,Definiciones}/Responses.cs`).
 */
@Injectable({ providedIn: 'root' })
export class WorkflowInstanceStatusService {
  private readonly http = inject(HttpClient);

  obtenerInstancia(id: string): Observable<WorkflowInstance> {
    return this.http.get<WorkflowInstance>(`/api/v1/workflows/instancias/${id}`);
  }

  /** Lista completa, SIN paginar -- mismo criterio que el backend real (`docs/guia-workflow.md`: "volumen
   * acotado por el propio grafo"). El orden de llegada NO está garantizado por el endpoint; el consumidor
   * (`WorkflowInstanceStatus`) los ordena por `fechaUtc` antes de mostrarlos. */
  obtenerHistorial(instanceId: string): Observable<readonly WorkflowHistorialEntry[]> {
    return this.http.get<readonly WorkflowHistorialEntry[]>(
      `/api/v1/workflows/instancias/${instanceId}/historial`,
    );
  }

  /** Grafo completo (estados + transiciones) de una versión -- necesario para resolver el NOMBRE del
   * estado actual de una instancia (`WorkflowInstance.estadoActualId` es sólo un Guid, ver
   * `workflow-instance.model.ts`) y las transiciones salientes disponibles. */
  obtenerVersion(versionId: string): Observable<WorkflowVersion> {
    return this.http.get<WorkflowVersion>(`/api/v1/workflows/versiones/${versionId}`);
  }
}
