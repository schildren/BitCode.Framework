// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58311/"}
// La URL base del entorno jsdom coincide con el puerto de `WorkflowTestServer`: como
// `WorkflowTaskActionsService` usa rutas relativas (`/api/v1/workflows/tareas/...`, igual que un
// consumidor real detrás del mismo origen que el backend), el `HttpClient` real las resuelve contra esa
// URL sin necesidad de tocar ningún campo privado del servicio.
import { HttpErrorResponse, provideHttpClient, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { WorkflowTestServer } from '../testing/workflow-test-server';
import { WorkflowTaskActionsService } from './workflow-task-actions.service';

const PORT = 58311;

describe('WorkflowTaskActionsService (contra un servidor HTTP real, mismo contrato que Workflow)', () => {
  let server: WorkflowTestServer;

  beforeAll(async () => {
    server = new WorkflowTestServer();
    await server.start(PORT);
  });

  afterAll(async () => {
    await server.stop();
  });

  function setup(): WorkflowTaskActionsService {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr())] });
    return TestBed.inject(WorkflowTaskActionsService);
  }

  it('obtiene una tarea real por id', async () => {
    const service = setup();
    const tarea = await firstValueFrom(service.obtenerTarea('task-1'));
    expect(tarea.id).toBe('task-1');
    expect(tarea.titulo).toBe('Revisar solicitud');
  });

  it('resuelve una tarea con éxito (200)', async () => {
    const service = setup();
    // El backend responde 200 sin body -- `HttpClient` deserializa un cuerpo vacío como `null`, no
    // `undefined` (comportamiento real de Angular, no un detalle a ocultar).
    await expect(firstValueFrom(service.resolver('task-1', 'Aprobar', 'ok'))).resolves.toBeNull();
  });

  it('propaga 403 cuando el actor no es el asignado actual (task-forbidden)', async () => {
    const service = setup();
    await expect(firstValueFrom(service.resolver('task-forbidden', 'Aprobar'))).rejects.toBeInstanceOf(
      HttpErrorResponse,
    );
  });

  it('propaga 409 cuando la acción no corresponde a ninguna transición válida (task-conflict)', async () => {
    const service = setup();
    try {
      await firstValueFrom(service.resolver('task-conflict', 'Aprobar'));
      expect.unreachable('debía rechazar con 409');
    } catch (error) {
      expect(error).toBeInstanceOf(HttpErrorResponse);
      expect((error as HttpErrorResponse).status).toBe(409);
    }
  });

  it('delegar envía el nuevo asignado y resuelve 200', async () => {
    const service = setup();
    await expect(firstValueFrom(service.delegar('task-1', 'user-2'))).resolves.toBeNull();
  });
});
