import http from 'node:http';
import { URL } from 'node:url';

/**
 * Servidor HTTP real (Node `http`, no un mock de `HttpClient`) que reproduce el contrato REAL de
 * TaskInbox/Workflow verificado en el código del backend -- mismo patrón que `ProblemDetailsTestServer`
 * (`@bitcode/core/testing`) y `BffTestDouble` (`@bitcode/auth/testing`): un doble de infraestructura real,
 * no una simulación de `HttpClient`.
 *
 * Rutas cubiertas, cada una con la forma REAL verificada contra el backend (ver comentarios inline con la
 * referencia a la fuente):
 *
 * - `GET /api/v1/taskinbox/bandeja` -- paginado (`PagedResult<TaskInboxItemResponse>`,
 *   `src/Shared.Kernel/PagedResult.cs` + `src/Platform/BitCode.Platform.TaskInbox/Bandeja/Responses.cs`),
 *   con los filtros REALMENTE soportados (`estado`, `workflowInstanceId`) -- ver
 *   `docs/guia-taskinbox.md`.
 * - `GET /api/v1/workflows/tareas/:id` -- `WorkflowTaskResponse`.
 * - `POST /api/v1/workflows/tareas/:id/resolver` -- 200, o 403/409 para ids reservados de prueba
 *   (`task-forbidden`/`task-conflict`), reproduciendo el `ProblemDetails` real de
 *   `ResultExtensions.ToProblemDetails` para un `Result.Failure` de negocio.
 * - `POST /api/v1/workflows/tareas/:id/delegar` -- mismo criterio que `resolver`.
 * - `GET /api/v1/workflows/instancias/:id` -- `WorkflowInstanceResponse`.
 * - `GET /api/v1/workflows/instancias/:id/historial` -- `WorkflowHistorialResponse[]`, sin paginar.
 * - `GET /api/v1/workflows/versiones/:id` -- `WorkflowVersionResponse` (grafo completo).
 */
export class WorkflowTestServer {
  private readonly server: http.Server;
  // XHR/keep-alive puede dejar sockets abiertos entre requests -- sin destruirlos explícitamente,
  // `server.close()` (llamado desde `stop()`) espera indefinidamente a que se cierren solos, lo que
  // cuelga el hook `afterAll` de vitest. Mismo patrón que `ProblemDetailsTestServer` (`@bitcode/core`).
  private readonly sockets = new Set<import('node:net').Socket>();

  /** Dataset fijo para paginación real (2 páginas de 10 + 1 de 3 con `pageSize=10`). */
  private readonly taskInboxItems = Array.from({ length: 23 }, (_, index) => ({
    id: `task-${index + 1}`,
    workflowInstanceId: 'instance-1',
    asignadoAUserId: 'user-1',
    estado: index % 5 === 0 ? 2 : index % 3 === 0 ? 1 : 0, // mezcla Pendiente/Aprobada/Rechazada
    asignadaAtUtc: new Date(2026, 0, index + 1).toISOString(),
    resueltaPorUserId: null as string | null,
    resueltaAtUtc: null as string | null,
    leidoAtUtc: null as string | null,
  }));

  constructor() {
    this.server = http.createServer((req, res) => this.handle(req, res));
    this.server.on('connection', (socket) => {
      this.sockets.add(socket);
      socket.on('close', () => this.sockets.delete(socket));
    });
  }

  async start(port: number): Promise<void> {
    await new Promise<void>((resolve) => this.server.listen(port, '127.0.0.1', resolve));
  }

  async stop(): Promise<void> {
    for (const socket of this.sockets) {
      socket.destroy();
    }
    this.sockets.clear();
    await new Promise<void>((resolve, reject) =>
      this.server.close((error) => (error ? reject(error) : resolve())),
    );
  }

  private json(res: http.ServerResponse, status: number, body: unknown): void {
    res.statusCode = status;
    res.setHeader('Content-Type', 'application/json');
    res.end(JSON.stringify(body));
  }

  private problem(res: http.ServerResponse, status: number, title: string, detail?: string): void {
    res.statusCode = status;
    res.setHeader('Content-Type', 'application/problem+json');
    res.end(JSON.stringify({ type: 'about:blank', title, detail, status }));
  }

  private handle(req: http.IncomingMessage, res: http.ServerResponse): void {
    const url = new URL(req.url ?? '/', 'http://127.0.0.1');
    const path = url.pathname;

    if (req.method === 'GET' && path === '/api/v1/taskinbox/bandeja') {
      this.handleListarBandeja(url, res);
      return;
    }

    const tareaMatch = path.match(/^\/api\/v1\/workflows\/tareas\/([^/]+)(\/(resolver|delegar))?$/);
    if (tareaMatch) {
      this.handleTareas(req, res, tareaMatch[1], tareaMatch[3]);
      return;
    }

    const historialMatch = path.match(/^\/api\/v1\/workflows\/instancias\/([^/]+)\/historial$/);
    if (req.method === 'GET' && historialMatch) {
      this.handleHistorial(res, historialMatch[1]);
      return;
    }

    const instanciaMatch = path.match(/^\/api\/v1\/workflows\/instancias\/([^/]+)$/);
    if (req.method === 'GET' && instanciaMatch) {
      this.handleInstancia(res, instanciaMatch[1]);
      return;
    }

    const versionMatch = path.match(/^\/api\/v1\/workflows\/versiones\/([^/]+)$/);
    if (req.method === 'GET' && versionMatch) {
      this.handleVersion(res, versionMatch[1]);
      return;
    }

    res.statusCode = 404;
    res.end();
  }

  private handleListarBandeja(url: URL, res: http.ServerResponse): void {
    const page = Number(url.searchParams.get('page') ?? '1');
    const pageSize = Number(url.searchParams.get('pageSize') ?? '20');
    const estadoParam = url.searchParams.get('estado');
    const workflowInstanceIdParam = url.searchParams.get('workflowInstanceId');

    let filtered = this.taskInboxItems;
    if (estadoParam !== null) {
      const estado = Number(estadoParam);
      filtered = filtered.filter((item) => item.estado === estado);
    }
    if (workflowInstanceIdParam) {
      filtered = filtered.filter((item) => item.workflowInstanceId === workflowInstanceIdParam);
    }

    const start = (page - 1) * pageSize;
    const items = filtered.slice(start, start + pageSize);

    this.json(res, 200, { items, page, pageSize, totalCount: filtered.length });
  }

  private handleTareas(
    req: http.IncomingMessage,
    res: http.ServerResponse,
    id: string,
    action: string | undefined,
  ): void {
    if (!action) {
      if (req.method !== 'GET') {
        res.statusCode = 405;
        res.end();
        return;
      }
      this.json(res, 200, {
        id,
        workflowInstanceId: 'instance-1',
        workflowStateId: 'state-pendiente',
        titulo: 'Revisar solicitud',
        asignadoAUserId: 'user-1',
        estado: 0,
        accionResuelta: null,
        slaVencimientoUtc: new Date(2026, 0, 15).toISOString(),
        escalada: false,
      });
      return;
    }

    if (req.method !== 'POST') {
      res.statusCode = 405;
      res.end();
      return;
    }

    this.readBody(req).then(() => {
      if (id === 'task-forbidden') {
        this.problem(res, 403, 'Workflow.Tareas.NoAutorizado', 'Solo el actor actualmente asignado puede resolver esta tarea.');
        return;
      }
      if (id === 'task-conflict') {
        this.problem(res, 409, 'Workflow.Tareas.TransicionInvalida', 'La acción no corresponde a ninguna transición válida.');
        return;
      }
      if (id === 'task-not-found') {
        this.problem(res, 404, 'Workflow.Tareas.NoEncontrada', `No existe la tarea ${id}.`);
        return;
      }

      res.statusCode = 200;
      res.end();
    });
  }

  private handleInstancia(res: http.ServerResponse, id: string): void {
    if (id === 'instance-not-found') {
      this.problem(res, 404, 'Workflow.Instancias.NoEncontrada', `No existe la instancia ${id}.`);
      return;
    }

    this.json(res, 200, {
      id,
      workflowDefinitionId: 'definition-1',
      workflowVersionId: 'version-1',
      estadoActualId: 'state-pendiente',
      estado: 0,
      variables: { monto: '1500' },
      finalizadaAtUtc: null,
    });
  }

  private handleHistorial(res: http.ServerResponse, instanceId: string): void {
    this.json(res, 200, [
      {
        id: 'hist-2',
        fechaUtc: new Date(2026, 0, 2).toISOString(),
        tipoEvento: 'TareaAsignada',
        detalle: `Tarea asignada en la instancia ${instanceId}.`,
        actorUserId: null,
      },
      {
        id: 'hist-1',
        fechaUtc: new Date(2026, 0, 1).toISOString(),
        tipoEvento: 'InstanciaIniciada',
        detalle: `Instancia ${instanceId} iniciada.`,
        actorUserId: 'user-admin',
      },
    ]);
  }

  private handleVersion(res: http.ServerResponse, id: string): void {
    this.json(res, 200, {
      id,
      workflowDefinitionId: 'definition-1',
      numero: 1,
      estado: 1,
      publicadaAtUtc: new Date(2025, 11, 1).toISOString(),
      estados: [
        {
          id: 'state-pendiente',
          codigo: 'PENDIENTE',
          nombre: 'Pendiente de revisión',
          esInicial: true,
          esFinal: false,
          requiereTarea: true,
          tituloTarea: 'Revisar solicitud',
          asignadoPorDefectoUserId: 'user-1',
          slaMinutos: 60,
          escalarAUserId: 'user-supervisor',
        },
        {
          id: 'state-aprobado',
          codigo: 'APROBADO',
          nombre: 'Aprobado',
          esInicial: false,
          esFinal: true,
          requiereTarea: false,
          tituloTarea: null,
          asignadoPorDefectoUserId: null,
          slaMinutos: null,
          escalarAUserId: null,
        },
        {
          id: 'state-rechazado',
          codigo: 'RECHAZADO',
          nombre: 'Rechazado',
          esInicial: false,
          esFinal: true,
          requiereTarea: false,
          tituloTarea: null,
          asignadoPorDefectoUserId: null,
          slaMinutos: null,
          escalarAUserId: null,
        },
      ],
      transiciones: [
        {
          id: 'transition-1',
          desdeEstadoId: 'state-pendiente',
          haciaEstadoId: 'state-aprobado',
          accion: 'Aprobar',
          reglaExpresion: null,
          orden: 1,
        },
        {
          id: 'transition-2',
          desdeEstadoId: 'state-pendiente',
          haciaEstadoId: 'state-rechazado',
          accion: 'Rechazar',
          reglaExpresion: null,
          orden: 2,
        },
      ],
    });
  }

  private async readBody(req: http.IncomingMessage): Promise<string> {
    const chunks: Buffer[] = [];
    for await (const chunk of req) {
      chunks.push(chunk as Buffer);
    }
    return Buffer.concat(chunks).toString('utf-8');
  }
}
