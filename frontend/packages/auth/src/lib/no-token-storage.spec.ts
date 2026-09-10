// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58215/"}
//
// Evidencia explícita del criterio de aceptación de F7-03 ("sin almacenar tokens inseguros"): ejercita
// un ciclo completo (resolver sesión, invocar una API protegida, cerrar sesión) contra un doble real del
// BFF y confirma en cada paso que ningún dato de sesión/token terminó en `localStorage`/`sessionStorage`
// -- la única persistencia de sesión del lado cliente es la cookie HttpOnly, que este mismo código nunca
// lee ni escribe directamente (ver `session.service.ts`/`auth.service.ts`).
import { HttpClient, provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { BITCODE_AUTH_CONFIG, DEFAULT_BITCODE_AUTH_CONFIG } from './config/auth-config';
import { BitcodeWindowLike, BITCODE_WINDOW } from './config/window.token';
import { BitcodeAuthService } from './actions/auth.service';
import { BitcodeSessionService } from './session/session.service';
import { BffTestDouble } from './testing/bff-test-double';
import { bitcodeAuthInterceptor } from './interceptors/auth.interceptor';

const PORT = 58215;
const BASE_URL = `http://127.0.0.1:${PORT}`;

class FakeWindow implements BitcodeWindowLike {
  readonly location = {
    assign: (): void => {
      /* no-op: sólo interesa que no se escriba nada en localStorage/sessionStorage */
    },
    origin: BASE_URL,
    pathname: '/',
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

function assertNoAuthDataInWebStorage(): void {
  for (const storage of [localStorage, sessionStorage]) {
    expect(storage.length).toBe(0);
    for (let i = 0; i < storage.length; i += 1) {
      const key = storage.key(i) ?? '';
      const value = storage.getItem(key) ?? '';
      expect(`${key}=${value}`).not.toMatch(/token|jwt|bearer|bc-bff-session/i);
    }
  }
}

describe('Criterio de aceptación F7-03: sin tokens inseguros en el cliente', () => {
  let server: BffTestDouble;

  beforeAll(async () => {
    server = new BffTestDouble();
    await server.start(PORT);
  });

  afterAll(async () => {
    await server.stop();
  });

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        // withXhr(): ver comentario equivalente en session.service.spec.ts -- necesario únicamente
        // para que jsdom persista la cookie de sesión entre peticiones dentro del test.
        provideHttpClient(withXhr(), withInterceptors([bitcodeAuthInterceptor])),
        { provide: BITCODE_WINDOW, useValue: new FakeWindow() },
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

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it('localStorage/sessionStorage están vacíos antes de resolver la sesión', () => {
    assertNoAuthDataInWebStorage();
  });

  it('resolver una sesión autenticada no escribe nada en localStorage/sessionStorage', async () => {
    const sessionId = server.seedSession({
      subject: 'user-1',
      userName: 'jdoe',
      roles: ['Admin'],
      tenantId: 'tenant-1',
    });
    await seedCookieInBrowser(sessionId);

    const session = TestBed.inject(BitcodeSessionService);
    const result = await session.checkSession();

    expect(result.status).toBe('authenticated');
    assertNoAuthDataInWebStorage();
  });

  it('llamar a una API protegida a través del interceptor no escribe nada en localStorage/sessionStorage', async () => {
    const sessionId = server.seedSession({ subject: 'user-1', roles: [] });
    await seedCookieInBrowser(sessionId);

    const http = TestBed.inject(HttpClient);
    await firstValueFrom(http.get(`${BASE_URL}/bff/api/ping`));

    assertNoAuthDataInWebStorage();
  });

  it('logout() no escribe nada en localStorage/sessionStorage', async () => {
    const sessionId = server.seedSession({ subject: 'user-1', roles: [] });
    await seedCookieInBrowser(sessionId);

    const auth = TestBed.inject(BitcodeAuthService);
    await auth.logout();

    assertNoAuthDataInWebStorage();
  });
});
