// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58211/"}
import { provideHttpClient, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { BITCODE_AUTH_CONFIG, DEFAULT_BITCODE_AUTH_CONFIG } from '../config/auth-config';
import { BffTestDouble } from '../testing/bff-test-double';
import { BitcodeSessionService } from './session.service';

const PORT = 58211;
const BASE_URL = `http://127.0.0.1:${PORT}`;

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

describe('BitcodeSessionService (contra un doble real del BFF)', () => {
  let server: BffTestDouble;

  beforeAll(async () => {
    server = new BffTestDouble();
    await server.start(PORT);
  });

  afterAll(async () => {
    await server.stop();
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        // withXhr(): Angular 22 usa `fetch` como backend por defecto, cuyo Node/undici subyacente en
        // Vitest+jsdom no implementa un cookie jar (a diferencia de un navegador real, donde `fetch`
        // con `credentials:'include'` sí envía/recibe cookies). Forzamos el backend XHR únicamente en
        // el arnés de test porque jsdom SÍ implementa un cookie jar real sobre `XMLHttpRequest` --
        // verificado explícitamente antes de escribir esta suite (ver reporte de la tarea F7-03). En
        // una app real, cualquiera de los dos backends funciona igual de bien con cookies de sesión.
        provideHttpClient(withXhr()),
        {
          provide: BITCODE_AUTH_CONFIG,
          useValue: { ...DEFAULT_BITCODE_AUTH_CONFIG, sessionEndpoint: `${BASE_URL}/bff/session` },
        },
      ],
    });
  });

  it('sin cookie de sesión, checkSession() resuelve "anonymous" sin lanzar', async () => {
    const service = TestBed.inject(BitcodeSessionService);

    const result = await service.checkSession();

    expect(result.status).toBe('anonymous');
    expect(result.claims).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
  });

  it('con una sesión válida, checkSession() resuelve "authenticated" con los claims reales', async () => {
    const sessionId = server.seedSession({
      subject: 'user-1',
      userName: 'jdoe',
      email: 'jdoe@bitcode.local',
      roles: ['Admin', 'Ventas'],
      tenantId: 'tenant-1',
    });
    await seedCookieInBrowser(sessionId);

    const service = TestBed.inject(BitcodeSessionService);
    const result = await service.checkSession();

    expect(result.status).toBe('authenticated');
    expect(result.claims?.subject).toBe('user-1');
    expect(result.claims?.roles).toEqual(['Admin', 'Ventas']);
    expect(service.isAuthenticated()).toBe(true);
    expect(service.claims()?.tenantId).toBe('tenant-1');
  });

  it('checkSession() concurrente comparte una sola petición HTTP', async () => {
    const service = TestBed.inject(BitcodeSessionService);

    const [first, second] = await Promise.all([service.checkSession(), service.checkSession()]);

    expect(first).toBe(second);
  });

  it('markAnonymous() refleja "sin sesión" de inmediato sin llamar al backend', () => {
    const service = TestBed.inject(BitcodeSessionService);

    service.markAnonymous();

    expect(service.status()).toBe('anonymous');
    expect(service.claims()).toBeNull();
  });
});
