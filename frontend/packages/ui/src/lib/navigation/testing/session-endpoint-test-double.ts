import http from 'node:http';

export interface FakeSessionClaims {
  readonly subject: string;
  readonly roles: readonly string[];
  readonly permissions?: readonly string[];
}

/**
 * Servidor HTTP real (Node `http`, no un mock de `HttpClient`/del `signal` de sesión) que implementa
 * ÚNICAMENTE el subconjunto del contrato BFF (`GET /bff/session`, ver `docs/guia-frontend-auth.md`)
 * necesario para probar `BitcodeMenuService` (F7-05) de punta a punta contra `BitcodeSessionService` real.
 *
 * Deliberadamente NO es el mismo `BffTestDouble` de `packages/auth/src/lib/testing/` (que vive en
 * `src/lib/testing/`, excluido del build publicable de `@bitcode/auth` -- ver `tsconfig.lib.json` de ese
 * paquete): reexportarlo desde el índice público de `@bitcode/auth` para reutilizarlo acá rompía el build
 * de esa librería (el archivo usa `node:http`, y el `tsconfig.lib.json` de `@bitcode/auth` no incluye
 * tipos de Node a propósito, porque es código de navegador). Duplicar aquí el subconjunto mínimo
 * (`/bff/session` + sembrado de cookie) es más simple y honesto que forzar una reexportación cruzada que
 * degradaría el build de producción de `@bitcode/auth`. Este archivo vive bajo `testing/`, excluido del
 * build publicable de `@bitcode/ui` por el mismo motivo (ver `tsconfig.lib.json` de este paquete).
 */
export class SessionEndpointTestDouble {
  private readonly server: http.Server;
  private readonly sessionsById = new Map<string, FakeSessionClaims>();

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

  seedSession(claims: FakeSessionClaims): string {
    const sessionId = `test-session-${Math.random().toString(36).slice(2)}`;
    this.sessionsById.set(sessionId, claims);
    return sessionId;
  }

  private readSessionCookie(req: http.IncomingMessage): string | undefined {
    const header = req.headers.cookie;
    if (!header) {
      return undefined;
    }
    const match = header
      .split(';')
      .map((part) => part.trim())
      .find((part) => part.startsWith('bc-bff-session='));
    return match?.substring('bc-bff-session='.length);
  }

  private handle(req: http.IncomingMessage, res: http.ServerResponse): void {
    const sessionId = this.readSessionCookie(req);
    const claims = sessionId ? this.sessionsById.get(sessionId) : undefined;

    // Endpoint de harness (no forma parte del contrato BFF real): simula que el navegador ya recibió la
    // cookie de sesión HttpOnly real vía `Set-Cookie` (equivalente al `/test/seed-cookie/:id` de
    // `BffTestDouble` en `@bitcode/auth`).
    if (req.method === 'GET' && req.url?.startsWith('/test/seed-cookie/')) {
      const id = req.url.substring('/test/seed-cookie/'.length);
      res.setHeader('Set-Cookie', `bc-bff-session=${id}; HttpOnly; SameSite=Strict; Path=/`);
      res.statusCode = 204;
      res.end();
      return;
    }

    if (req.method === 'GET' && req.url === '/bff/session') {
      if (!claims) {
        res.statusCode = 401;
        res.end();
        return;
      }
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify(claims));
      return;
    }

    res.statusCode = 404;
    res.end();
  }
}
