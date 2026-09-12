import http from 'node:http';

/**
 * Servidor HTTP real (Node `http`, no un mock de `HttpClient`) que devuelve respuestas de error tal como
 * las produce hoy el backend real de BitCode -- mismo patrón que `BffTestDouble`
 * (`@bitcode/auth/testing`) y `TestExternalHttpServer`/`TestReportingHttpServer` (backend .NET): un doble
 * de infraestructura real, no una simulación de `HttpClient`.
 *
 * Cada ruta reproduce una forma REAL verificada en el código del backend (ver
 * `problem-details.model.ts`), no una forma inventada:
 *
 * - `/validation` → 400, `ValidationProblemDetails` (`TypedResults.ValidationProblem`, forma real de
 *   `ResultExtensions.ToProblemDetails` para un `ValidationError`).
 * - `/not-found` → 404, `ProblemDetails` estándar (`TypedResults.Problem`, `Result.Failure` de negocio).
 * - `/conflict` → 409, ídem, para un conflicto de negocio.
 * - `/unhandled` → 500, la forma NO estándar de `GlobalExceptionHandler` (`{type, title, status,
 *   traceId}`, sin `detail`/`instance`) -- el único caso real donde el backend expone algo parecido a un
 *   correlation id hoy.
 * - `/non-json-error` → 502 con un body HTML/texto plano, simulando un proxy intermedio (p. ej. un
 *   API Gateway) que devuelve un error SIN pasar por ningún `ProblemDetails` del backend -- el caso
 *   explícito pedido por la tarea para verificar degradación sin excepción no controlada.
 * - `/ok` → 200, para verificar que un request exitoso no pasa por ninguna rama de error.
 */
export class ProblemDetailsTestServer {
  private readonly server: http.Server;

  constructor() {
    this.server = http.createServer((req, res) => this.handle(req, res));
  }

  async start(port: number): Promise<void> {
    await new Promise<void>((resolve) => this.server.listen(port, '127.0.0.1', resolve));
  }

  async stop(): Promise<void> {
    await new Promise<void>((resolve, reject) =>
      this.server.close((error) => (error ? reject(error) : resolve())),
    );
  }

  private json(res: http.ServerResponse, status: number, body: unknown): void {
    res.statusCode = status;
    res.setHeader('Content-Type', 'application/problem+json');
    res.end(JSON.stringify(body));
  }

  private handle(req: http.IncomingMessage, res: http.ServerResponse): void {
    if (req.method === 'GET' && req.url === '/ok') {
      res.statusCode = 200;
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify({ ok: true }));
      return;
    }

    if (req.method === 'POST' && req.url === '/validation') {
      this.json(res, 400, {
        type: 'https://tools.ietf.org/html/rfc7231#section-6.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: {
          'Producto.NombreRequerido': ['El nombre es requerido.'],
        },
      });
      return;
    }

    if (req.method === 'GET' && req.url === '/not-found') {
      this.json(res, 404, {
        type: 'https://tools.ietf.org/html/rfc7231#section-6.5.4',
        title: 'Producto.NoEncontrado',
        detail: 'No se encontró el producto con el id solicitado.',
        status: 404,
      });
      return;
    }

    if (req.method === 'POST' && req.url === '/conflict') {
      this.json(res, 409, {
        type: 'https://tools.ietf.org/html/rfc7231#section-6.5.8',
        title: 'Concurrency.Conflict',
        detail: 'El recurso fue modificado por otra operación concurrente.',
        status: 409,
      });
      return;
    }

    if (req.method === 'GET' && req.url === '/unauthorized') {
      this.json(res, 401, {
        type: 'https://tools.ietf.org/html/rfc7235#section-3.1',
        title: 'Sesion.Invalida',
        status: 401,
      });
      return;
    }

    if (req.method === 'GET' && req.url === '/unhandled') {
      this.json(res, 500, {
        type: 'https://tools.ietf.org/html/rfc7231#section-6.6.1',
        title: 'Ha ocurrido un error inesperado',
        status: 500,
        traceId: '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01',
      });
      return;
    }

    if (req.method === 'GET' && req.url === '/non-json-error') {
      res.statusCode = 502;
      res.setHeader('Content-Type', 'text/html');
      res.end('<html><body>502 Bad Gateway</body></html>');
      return;
    }

    if (req.method === 'GET' && req.url === '/empty-error') {
      res.statusCode = 503;
      res.end();
      return;
    }

    res.statusCode = 404;
    res.end();
  }
}
