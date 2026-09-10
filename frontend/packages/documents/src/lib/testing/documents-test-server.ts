import http from 'node:http';
import { URL } from 'node:url';

/**
 * Servidor HTTP real (Node `http`) que reproduce el contrato REAL de Documents verificado contra el código
 * del backend (`src/Platform/BitCode.Platform.Documents`) -- mismo patrón que `WorkflowTestServer`
 * (`@bitcode/workflow`) y `ProblemDetailsTestServer`/`BffTestDouble`. No parsea el `multipart/form-data`
 * completo (no hace falta para las pruebas de este paquete: lo que se verifica es que el cliente reporta
 * progreso real y maneja la respuesta/errores, no que el parser multipart del backend sea correcto -- eso
 * ya lo cubren las pruebas de integración .NET del propio módulo Documents) -- sólo drena el body y
 * responde según la ruta/id, igual que `WorkflowTestServer` con `readBody`.
 */
export class DocumentsTestServer {
  private readonly server: http.Server;
  private readonly sockets = new Set<import('node:net').Socket>();

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

    if (req.method === 'POST' && path === '/api/v1/documentos') {
      this.readBody(req).then(() => this.json(res, 201, 'documento-nuevo-1'));
      return;
    }

    if (req.method === 'GET' && path === '/api/v1/documentos') {
      this.handleListar(url, res);
      return;
    }

    const versionesMatch = path.match(/^\/api\/v1\/documentos\/([^/]+)\/versiones$/);
    if (versionesMatch) {
      this.handleVersiones(req, res, versionesMatch[1]);
      return;
    }

    const descargarMatch = path.match(/^\/api\/v1\/documentos\/([^/]+)\/descargar$/);
    if (req.method === 'GET' && descargarMatch) {
      this.handleDescargar(url, res, descargarMatch[1]);
      return;
    }

    const idMatch = path.match(/^\/api\/v1\/documentos\/([^/]+)$/);
    if (idMatch) {
      this.handleDocumento(req, res, idMatch[1]);
      return;
    }

    res.statusCode = 404;
    res.end();
  }

  private handleListar(url: URL, res: http.ServerResponse): void {
    const page = Number(url.searchParams.get('page') ?? '1');
    const pageSize = Number(url.searchParams.get('pageSize') ?? '20');
    const items = Array.from({ length: pageSize }, (_, index) => ({
      id: `doc-${(page - 1) * pageSize + index + 1}`,
      titulo: `Documento ${(page - 1) * pageSize + index + 1}`,
      descripcion: null,
      clasificacion: 'Contrato',
      retencionDias: 365,
      versionActualId: 'version-1',
      versionActualNumero: 1,
      disponibleParaDisposicionDesde: new Date(2027, 0, 1).toISOString(),
    }));
    this.json(res, 200, { items, page, pageSize, totalCount: 45 });
  }

  private handleDocumento(req: http.IncomingMessage, res: http.ServerResponse, id: string): void {
    if (req.method === 'GET') {
      if (id === 'doc-not-found') {
        this.problem(res, 404, 'Documents.Documentos.NoEncontrado', `No existe el documento ${id}.`);
        return;
      }
      this.json(res, 200, {
        id,
        titulo: 'Contrato de servicio',
        descripcion: 'Contrato marco 2026',
        clasificacion: 'Contrato',
        retencionDias: 365,
        versionActualId: 'version-1',
        versionActualNumero: 1,
        disponibleParaDisposicionDesde: new Date(2027, 0, 1).toISOString(),
      });
      return;
    }

    if (req.method === 'DELETE') {
      this.readBody(req).then(() => {
        if (id === 'doc-conflict') {
          this.problem(res, 409, 'Documents.Documentos.RetencionVigente', 'El documento aún está en período de retención.');
          return;
        }
        res.statusCode = 204;
        res.end();
      });
      return;
    }

    res.statusCode = 405;
    res.end();
  }

  private handleVersiones(req: http.IncomingMessage, res: http.ServerResponse, documentoId: string): void {
    if (req.method === 'GET') {
      this.json(res, 200, [
        {
          id: 'version-1',
          documentoId,
          numero: 1,
          nombreArchivo: 'contrato.pdf',
          contentType: 'application/pdf',
          tamanioBytes: 102400,
          hashSha256: 'ABCDEF0123456789',
          estadoEscaneo: 1,
        },
        {
          id: 'version-2',
          documentoId,
          numero: 2,
          nombreArchivo: 'contrato-v2.pdf',
          contentType: 'application/pdf',
          tamanioBytes: 204800,
          hashSha256: 'FEDCBA9876543210',
          estadoEscaneo: 0,
        },
      ]);
      return;
    }

    if (req.method === 'POST') {
      this.readBody(req).then(() => this.json(res, 201, 'version-nueva-1'));
      return;
    }

    res.statusCode = 405;
    res.end();
  }

  private handleDescargar(url: URL, res: http.ServerResponse, id: string): void {
    const numero = url.searchParams.get('numero');

    if (id === 'doc-infectado' || numero === '99') {
      this.problem(res, 409, 'Documents.Versiones.NoDescargable', 'La versión no puede descargarse (escaneo pendiente o infectado).');
      return;
    }
    if (id === 'doc-not-found') {
      this.problem(res, 404, 'Documents.Documentos.NoEncontrado', `No existe el documento ${id}.`);
      return;
    }

    const contenido = Buffer.from('contenido-de-prueba', 'utf-8');
    res.statusCode = 200;
    res.setHeader('Content-Type', 'application/pdf');
    res.setHeader('Content-Disposition', `attachment; filename="contrato${numero ? `-v${numero}` : ''}.pdf"`);
    res.end(contenido);
  }

  private async readBody(req: http.IncomingMessage): Promise<string> {
    const chunks: Buffer[] = [];
    for await (const chunk of req) {
      chunks.push(chunk as Buffer);
    }
    return Buffer.concat(chunks).toString('utf-8');
  }
}
