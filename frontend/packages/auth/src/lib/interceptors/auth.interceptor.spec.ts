// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58213/"}
import { HttpClient, provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { BITCODE_AUTH_CONFIG, DEFAULT_BITCODE_AUTH_CONFIG } from '../config/auth-config';
import { BitcodeWindowLike, BITCODE_WINDOW } from '../config/window.token';
import { BitcodeSessionService } from '../session/session.service';
import { BffTestDouble } from '../testing/bff-test-double';
import { bitcodeAuthInterceptor } from './auth.interceptor';

const PORT = 58213;
const BASE_URL = `http://127.0.0.1:${PORT}`;

class FakeWindow implements BitcodeWindowLike {
  readonly assignedUrls: string[] = [];
  readonly location = {
    assign: (url: string): void => {
      this.assignedUrls.push(url);
    },
    origin: BASE_URL,
    pathname: '/pedidos',
    search: '',
  };
}

function realXhr(path: string): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('GET', `${BASE_URL}${path}`);
    xhr.withCredentials = true;
    xhr.onload = () => resolve();
    xhr.onerror = () => reject(new Error(`Fallo la petición de harness a ${path}`));
    xhr.send();
  });
}

const seedCookieInBrowser = (sessionId: string): Promise<void> => realXhr(`/test/seed-cookie/${sessionId}`);

// La cookie de sesión es HttpOnly (por diseño): el propio test no puede borrarla con
// `document.cookie`, así que se pide al doble del BFF que la borre igual que lo haría un logout real.
const clearCookieInBrowser = (): Promise<void> => realXhr('/test/clear-cookie');

describe('bitcodeAuthInterceptor (contra un doble real del BFF)', () => {
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
        provideHttpClient(withXhr(), withInterceptors([bitcodeAuthInterceptor])),
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

  it('adjunta withCredentials a toda request saliente, incluso a una API arbitraria', async () => {
    const sessionId = server.seedSession({ subject: 'user-1', roles: ['Admin'] });
    await seedCookieInBrowser(sessionId);

    const http = TestBed.inject(HttpClient);
    const response = await firstValueFrom(http.get<{ pong: boolean }>(`${BASE_URL}/bff/api/ping`));

    expect(response.pong).toBe(true);
  });

  it('un 401 en una API protegida marca la sesión como anónima y redirige a login', async () => {
    await clearCookieInBrowser();
    const http = TestBed.inject(HttpClient);
    const session = TestBed.inject(BitcodeSessionService);

    await expect(firstValueFrom(http.get(`${BASE_URL}/bff/api/ping`))).rejects.toBeTruthy();

    expect(session.status()).toBe('anonymous');
    expect(fakeWindow.assignedUrls).toHaveLength(1);
    expect(new URL(fakeWindow.assignedUrls[0]).pathname).toBe('/auth/login');
  });

  it('un 401 al resolver la sesión actual (sessionEndpoint) NO dispara una redirección adicional', async () => {
    await clearCookieInBrowser();
    const http = TestBed.inject(HttpClient);

    await expect(firstValueFrom(http.get(`${BASE_URL}/bff/session`))).rejects.toBeTruthy();

    expect(fakeWindow.assignedUrls).toHaveLength(0);
  });
});
