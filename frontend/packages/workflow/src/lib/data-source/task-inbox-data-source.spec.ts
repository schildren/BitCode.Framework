// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58310/"}
import { HttpClient, provideHttpClient, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { WorkflowTestServer } from '../testing/workflow-test-server';
import { TaskInboxItemEstado } from '../models/task-inbox-item.model';
import { TaskInboxHttpDataSource } from './task-inbox-data-source';

const PORT = 58310;
const BASE_URL = `http://127.0.0.1:${PORT}/api/v1/taskinbox/bandeja`;

describe('TaskInboxHttpDataSource (contra un servidor HTTP real, mismo contrato que el backend)', () => {
  let server: WorkflowTestServer;

  beforeAll(async () => {
    server = new WorkflowTestServer();
    await server.start(PORT);
  });

  afterAll(async () => {
    await server.stop();
  });

  function setup(): HttpClient {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr())] });
    return TestBed.inject(HttpClient);
  }

  it('pagina server-side según page/pageSize, sin recortar un array en el cliente', async () => {
    const http = setup();
    const dataSource = new TaskInboxHttpDataSource(http, {}, BASE_URL);

    const firstPage = await firstValueFrom(
      dataSource.loadPage({ page: 1, pageSize: 10, sort: null, filter: { columns: {} } }),
    );
    expect(firstPage.items).toHaveLength(10);
    expect(firstPage.totalCount).toBe(23);
    expect(firstPage.items[0].id).toBe('task-1');

    const secondPage = await firstValueFrom(
      dataSource.loadPage({ page: 2, pageSize: 10, sort: null, filter: { columns: {} } }),
    );
    expect(secondPage.items).toHaveLength(10);
    expect(secondPage.items[0].id).toBe('task-11');
  });

  it('envía el filtro de estado como query param real, recalculando totalCount contra el subconjunto filtrado', async () => {
    const http = setup();
    const dataSource = new TaskInboxHttpDataSource(http, { estado: TaskInboxItemEstado.Rechazada }, BASE_URL);

    const result = await firstValueFrom(
      dataSource.loadPage({ page: 1, pageSize: 50, sort: null, filter: { columns: {} } }),
    );
    expect(result.items.length).toBe(result.totalCount);
    expect(result.items.every((item) => item.estado === TaskInboxItemEstado.Rechazada)).toBe(true);
  });

  it('ignora sort/filter.global del BitcodeGridPageRequest -- el endpoint real no los soporta (ver docs/guia-taskinbox.md)', async () => {
    const http = setup();
    const dataSource = new TaskInboxHttpDataSource(http, {}, BASE_URL);

    const result = await firstValueFrom(
      dataSource.loadPage({
        page: 1,
        pageSize: 10,
        sort: { columnId: 'asignadaAtUtc', direction: 'desc' },
        filter: { global: 'cualquier cosa', columns: {} },
      }),
    );
    // Mismo resultado que sin sort/filter -- confirma que no se envían/tienen efecto.
    expect(result.items[0].id).toBe('task-1');
    expect(result.totalCount).toBe(23);
  });
});
