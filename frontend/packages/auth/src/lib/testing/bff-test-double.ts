import http from 'node:http';

export interface FakeUserClaims {
  readonly subject: string;
  readonly userName?: string;
  readonly email?: string;
  readonly roles: readonly string[];
  readonly tenantId?: string;
  /** Ver `BitcodeUserClaims.permissions` (F7-04) -- opcional porque ningún host real lo publica todavía;
   * el doble lo soporta para poder probar el mapeo cliente y el endpoint protegido de abajo. */
  readonly permissions?: readonly string[];
}

/**
 * Servidor HTTP real (Node `http`, no un mock de `HttpClient`) que implementa el subconjunto del
 * contrato BFF/OIDC documentado en `docs/guia-oidc-adapter.md` (F2-02/F2-03/F2-06) necesario para probar
 * `@bitcode/auth` de punta a punta con peticiones HTTP reales: cookie de sesión HttpOnly
 * (`bc-bff-session`), `POST /auth/logout` y `POST /auth/logout-all`, y un endpoint protegido de ejemplo
 * (`GET /bff/api/ping`, representando lo que el proxy YARP del BFF reenviaría a una API real).
 *
 * Desviaciones deliberadas y documentadas respecto del contrato real (ver `docs/guia-frontend-auth.md`,
 * sección "Cómo se probó" de la tarea F7-03):
 *
 * - No fija el atributo `Secure` en la cookie: el doble corre sobre HTTP plano en el entorno de test; un
 *   navegador real rechazaría almacenar una cookie `Secure` recibida por HTTP. El backend real SÍ debe
 *   fijarla (y la fija, ver `BffAuthenticationServiceCollectionExtensions`).
 * - `GET /bff/session` no existe todavía en ningún host BFF real de BitCode -- no está documentado en
 *   `docs/guia-oidc-adapter.md`, que no define ningún endpoint "quién soy". Es el contrato que
 *   `@bitcode/auth` propone y necesita; agregarlo al backend real queda como pendiente explícito (ver
 *   reporte de la tarea F7-03), no inventado ni prometido como ya resuelto.
 * - No implementa el intercambio real de `code` por tokens, PKCE, ni el proxy YARP en sí -- eso ya está
 *   resuelto y probado a nivel de backend (F2-02/F2-03/F2-05/F2-06); acá sólo se simula el resultado
 *   observable desde el navegador (cookie de sesión + respuestas JSON), no el mecanismo interno.
 */
export class BffTestDouble {
  private readonly server: http.Server;
  private readonly sessionsById = new Map<string, FakeUserClaims>();

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

  /**
   * Helper de test únicamente: registra una sesión válida server-side y devuelve el identificador de
   * sesión a usar. El código de `@bitcode/auth` bajo prueba nunca invoca este método -- sólo lo usa el
   * harness de test para preparar el escenario ("el usuario ya completó el login real vía IdP +
   * callback"), sin tener que reimplementar el flujo OIDC completo para poder probar el cliente.
   */
  seedSession(claims: FakeUserClaims): string {
    const sessionId = `test-session-${Math.random().toString(36).slice(2)}`;
    this.sessionsById.set(sessionId, claims);
    return sessionId;
  }

  hasActiveSessionsFor(subject: string): boolean {
    return [...this.sessionsById.values()].some((claims) => claims.subject === subject);
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

  private clearCookieHeader(res: http.ServerResponse): void {
    res.setHeader('Set-Cookie', 'bc-bff-session=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0');
  }

  private handle(req: http.IncomingMessage, res: http.ServerResponse): void {
    const sessionId = this.readSessionCookie(req);
    const claims = sessionId ? this.sessionsById.get(sessionId) : undefined;

    // Endpoint de harness (no forma parte del contrato BFF real): simula que el navegador terminó de
    // completar login/callback y ya recibió la cookie de sesión real vía `Set-Cookie`.
    if (req.method === 'GET' && req.url?.startsWith('/test/seed-cookie/')) {
      const id = req.url.substring('/test/seed-cookie/'.length);
      res.setHeader('Set-Cookie', `bc-bff-session=${id}; HttpOnly; SameSite=Strict; Path=/`);
      res.statusCode = 204;
      res.end();
      return;
    }

    // Endpoint de harness (no forma parte del contrato BFF real): borra la cookie de sesión del
    // navegador entre pruebas -- necesario porque es HttpOnly, así que el propio JavaScript de test no
    // puede borrarla vía `document.cookie` (por diseño, el mismo motivo por el que un XSS tampoco
    // podría).
    if (req.method === 'GET' && req.url === '/test/clear-cookie') {
      this.clearCookieHeader(res);
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

    if (req.method === 'POST' && req.url === '/auth/logout') {
      if (!claims || !sessionId) {
        res.statusCode = 401;
        res.end();
        return;
      }
      this.sessionsById.delete(sessionId);
      this.clearCookieHeader(res);
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify({}));
      return;
    }

    if (req.method === 'POST' && req.url === '/auth/logout-all') {
      if (!claims) {
        res.statusCode = 401;
        res.end();
        return;
      }
      for (const [id, value] of this.sessionsById.entries()) {
        if (value.subject === claims.subject) {
          this.sessionsById.delete(id);
        }
      }
      this.clearCookieHeader(res);
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify({}));
      return;
    }

    if (req.method === 'GET' && req.url === '/bff/api/ping') {
      if (!claims) {
        res.statusCode = 401;
        res.end();
        return;
      }
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify({ pong: true }));
      return;
    }

    // Endpoint de harness (F7-04): representa lo que en un backend real haría `[RequirePermission]`
    // (`Shared.Infrastructure.Security/Permissions/RequirePermissionAttribute.cs`) -- exige el permiso
    // `identidad.usuarios.crear` server-side, DEVUELTO por el propio servidor, no calculado por el
    // cliente. Existe únicamente para que `require-permission.guard.spec.ts` pueda demostrar que un 403
    // real del backend ocurre incluso si algo del lado cliente (guard, directiva) hubiese fallado o sido
    // sorteado -- la UI nunca es la última línea de defensa.
    if (req.method === 'POST' && req.url === '/bff/api/usuarios') {
      if (!claims) {
        res.statusCode = 401;
        res.end();
        return;
      }
      if (!(claims.permissions ?? []).includes('identidad.usuarios.crear')) {
        res.statusCode = 403;
        res.setHeader('Content-Type', 'application/json');
        res.end(JSON.stringify({ title: 'Forbidden', status: 403 }));
        return;
      }
      res.statusCode = 201;
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify({ id: 'user-creado' }));
      return;
    }

    res.statusCode = 404;
    res.end();
  }
}
