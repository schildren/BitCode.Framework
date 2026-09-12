// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58212/"}
import { provideHttpClient, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, RouterStateSnapshot } from '@angular/router';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { BITCODE_AUTH_CONFIG, DEFAULT_BITCODE_AUTH_CONFIG } from '../config/auth-config';
import { BitcodeWindowLike, BITCODE_WINDOW } from '../config/window.token';
import { BffTestDouble } from '../testing/bff-test-double';
import { bitcodeAuthGuard } from './auth.guard';

const PORT = 58212;
const BASE_URL = `http://127.0.0.1:${PORT}`;

class FakeWindow implements BitcodeWindowLike {
  readonly assignedUrls: string[] = [];
  readonly location = {
    assign: (url: string): void => {
      this.assignedUrls.push(url);
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

function runGuard(url: string): Promise<boolean> {
  return TestBed.runInInjectionContext(() =>
    Promise.resolve(bitcodeAuthGuard({} as never, { url } as RouterStateSnapshot)),
  ) as Promise<boolean>;
}

describe('bitcodeAuthGuard (contra un doble real del BFF)', () => {
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
        { provide: Router, useValue: {} },
        { provide: BITCODE_WINDOW, useValue: fakeWindow },
        {
          provide: BITCODE_AUTH_CONFIG,
          useValue: { ...DEFAULT_BITCODE_AUTH_CONFIG, sessionEndpoint: `${BASE_URL}/bff/session` },
        },
      ],
    });
  });

  it('sin sesión, deniega el acceso y redirige (window.location) al login con el returnUrl', async () => {
    const allowed = await runGuard('/pedidos/123');

    expect(allowed).toBe(false);
    expect(fakeWindow.assignedUrls).toHaveLength(1);
    const redirectUrl = new URL(fakeWindow.assignedUrls[0]);
    expect(redirectUrl.pathname).toBe('/auth/login');
    expect(redirectUrl.searchParams.get('returnUrl')).toBe('/pedidos/123');
  });

  it('con sesión activa, permite el acceso sin redirigir', async () => {
    const sessionId = server.seedSession({ subject: 'user-1', roles: ['Admin'] });
    await seedCookieInBrowser(sessionId);

    const allowed = await runGuard('/pedidos/123');

    expect(allowed).toBe(true);
    expect(fakeWindow.assignedUrls).toHaveLength(0);
  });
});
