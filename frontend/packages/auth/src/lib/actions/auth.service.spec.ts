// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58214/"}
import { provideHttpClient, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { BITCODE_AUTH_CONFIG, DEFAULT_BITCODE_AUTH_CONFIG } from '../config/auth-config';
import { BitcodeWindowLike, BITCODE_WINDOW } from '../config/window.token';
import { BitcodeSessionService } from '../session/session.service';
import { BffTestDouble } from '../testing/bff-test-double';
import { BitcodeAuthService } from './auth.service';

const PORT = 58214;
const BASE_URL = `http://127.0.0.1:${PORT}`;

class FakeWindow implements BitcodeWindowLike {
  readonly assignedUrls: string[] = [];
  readonly location = {
    assign: (url: string): void => {
      this.assignedUrls.push(url);
    },
    origin: BASE_URL,
    pathname: '/dashboard',
    search: '',
  };
}

async function seedCookieInBrowser(sessionId: string): Promise<void> {
  await new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('GET', `${BASE_URL}/test/seed-cookie/${sessionId}`);
    xhr.withCredentials = true;
    xhr.onload = () => resolve();
    xhr.onerror = () => reject(new Error('No se pudo sembrar la cookie de sesión de prueba.'));
    xhr.send();
  });
}

describe('BitcodeAuthService (contra un doble real del BFF)', () => {
  let server: BffTestDouble;
  let fakeWindow: FakeWindow;

  beforeAll(async () => {
    server = new BffTestDouble();
    await server.start(PORT);
  });

  afterAll(async () => {
    await server.stop();
  });

  beforeEach(() => {
    fakeWindow = new FakeWindow();
    TestBed.configureTestingModule({
      providers: [
        // withXhr(): ver comentario equivalente en session.service.spec.ts -- necesario únicamente
        // para que jsdom persista la cookie de sesión entre peticiones dentro del test.
        provideHttpClient(withXhr()),
        { provide: BITCODE_WINDOW, useValue: fakeWindow },
        {
          provide: BITCODE_AUTH_CONFIG,
          useValue: {
            ...DEFAULT_BITCODE_AUTH_CONFIG,
            sessionEndpoint: `${BASE_URL}/bff/session`,
            logoutEndpoint: `${BASE_URL}/auth/logout`,
            logoutAllEndpoint: `${BASE_URL}/auth/logout-all`,
          },
        },
      ],
    });
  });

  it('login() nunca hace fetch: solo reubica el navegador hacia /auth/login con el returnUrl', () => {
    const auth = TestBed.inject(BitcodeAuthService);

    auth.login('/pedidos/999');

    expect(fakeWindow.assignedUrls).toEqual([`${BASE_URL}/auth/login?returnUrl=%2Fpedidos%2F999`]);
  });

  it('logout() invoca POST /auth/logout, limpia el estado local y no navega si no hay end_session_endpoint', async () => {
    const sessionId = server.seedSession({ subject: 'user-1', roles: [] });
    await seedCookieInBrowser(sessionId);

    const auth = TestBed.inject(BitcodeAuthService);
    const session = TestBed.inject(BitcodeSessionService);
    await session.checkSession();
    expect(session.isAuthenticated()).toBe(true);

    await auth.logout();

    expect(session.isAuthenticated()).toBe(false);
    expect(session.status()).toBe('anonymous');
    expect(fakeWindow.assignedUrls).toHaveLength(0);

    // La sesión quedó revocada server-side: un checkSession() posterior confirma "anonymous" real,
    // no sólo el estado en memoria.
    const result = await session.checkSession();
    expect(result.status).toBe('anonymous');
  });

  it('logout() limpia el estado local incluso si la llamada al backend falla (sesión ya vencida)', async () => {
    const auth = TestBed.inject(BitcodeAuthService);
    const session = TestBed.inject(BitcodeSessionService);
    session.markAnonymous();

    await auth.logout();

    expect(session.status()).toBe('anonymous');
  });

  it('logoutAllDevices() revoca todas las sesiones del sujeto', async () => {
    const sessionA = server.seedSession({ subject: 'user-2', roles: [] });
    server.seedSession({ subject: 'user-2', roles: [] });
    await seedCookieInBrowser(sessionA);

    const auth = TestBed.inject(BitcodeAuthService);
    const session = TestBed.inject(BitcodeSessionService);
    await session.checkSession();
    expect(session.isAuthenticated()).toBe(true);

    await auth.logoutAllDevices();

    expect(session.status()).toBe('anonymous');
    expect(server.hasActiveSessionsFor('user-2')).toBe(false);
  });
});
