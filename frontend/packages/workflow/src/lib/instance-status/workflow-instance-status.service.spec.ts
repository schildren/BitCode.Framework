// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58312/"}
// Misma estrategia que `workflow-task-actions.service.spec.ts`: la URL base de jsdom coincide con el
// puerto de `WorkflowTestServer`, así que las rutas relativas del servicio se resuelven contra él sin
// tocar ningún campo privado.
import { provideHttpClient, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { WorkflowTestServer } from '../testing/workflow-test-server';
import { WorkflowInstanceStatusService } from './workflow-instance-status.service';

const PORT = 58312;

describe('WorkflowInstanceStatusService (contra un servidor HTTP real, mismo contrato que Workflow)', () => {
  let server: WorkflowTestServer;

  beforeAll(async () => {
    server = new WorkflowTestServer();
    await server.start(PORT);
  });

  afterAll(async () => {
    await server.stop();
  });

  function setup(): WorkflowInstanceStatusService {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr())] });
    return TestBed.inject(WorkflowInstanceStatusService);
  }

  it('obtiene una instancia real por id', async () => {
    const service = setup();
    const instancia = await firstValueFrom(service.obtenerInstancia('instance-1'));
    expect(instancia.id).toBe('instance-1');
    expect(instancia.workflowVersionId).toBe('version-1');
  });

  it('propaga 404 cuando la instancia no existe (instance-not-found)', async () => {
    const service = setup();
    await expect(firstValueFrom(service.obtenerInstancia('instance-not-found'))).rejects.toMatchObject({
      status: 404,
    });
  });

  it('obtiene el historial completo, sin paginar', async () => {
    const service = setup();
    const historial = await firstValueFrom(service.obtenerHistorial('instance-1'));
    expect(historial).toHaveLength(2);
    expect(historial.map((entry) => entry.tipoEvento)).toContain('InstanciaIniciada');
  });

  it('obtiene el grafo completo de una versión (estados + transiciones)', async () => {
    const service = setup();
    const version = await firstValueFrom(service.obtenerVersion('version-1'));
    expect(version.estados).toHaveLength(3);
    expect(version.transiciones.map((t) => t.accion)).toEqual(['Aprobar', 'Rechazar']);
  });
});
