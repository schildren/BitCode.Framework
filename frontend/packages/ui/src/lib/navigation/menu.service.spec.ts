// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58311/"}
import { BITCODE_AUTH_CONFIG, BitcodeSessionService, DEFAULT_BITCODE_AUTH_CONFIG } from '@bitcode/auth';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { provideBitcodeMenuItems } from './menu.config';
import { BitcodeMenuService } from './menu.service';
import { BitcodeMenuItem } from './menu-item.model';
import { SessionEndpointTestDouble } from './testing/session-endpoint-test-double';

const PORT = 58311;
const BASE_URL = `http://127.0.0.1:${PORT}`;

const MENU_ITEMS: BitcodeMenuItem[] = [
  { id: 'inicio', label: 'Inicio', link: '/inicio', order: 0 },
  {
    id: 'identidad',
    label: 'Identidad',
    order: 1,
    children: [
      {
        id: 'identidad-usuarios',
        label: 'Usuarios',
        link: '/identidad/usuarios',
        requiredPermissions: 'identidad.usuarios.ver',
      },
    ],
  },
];

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

describe('BitcodeMenuService (F7-05, contra un doble real del endpoint de sesión BFF)', () => {
  let server: SessionEndpointTestDouble;

  beforeAll(async () => {
    server = new SessionEndpointTestDouble();
    await server.start(PORT);
  });

  afterAll(async () => {
    await server.stop();
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        // Ver `session.service.spec.ts` (F7-03) para el porqué de `withXhr()` en el arnés de test.
        provideHttpClient(withXhr()),
        {
          provide: BITCODE_AUTH_CONFIG,
          useValue: { ...DEFAULT_BITCODE_AUTH_CONFIG, sessionEndpoint: `${BASE_URL}/bff/session` },
        },
        provideBitcodeMenuItems(MENU_ITEMS),
      ],
    });
  });

  it('sin sesión resuelta, sólo muestra los items sin permiso requerido', () => {
    const menu = TestBed.inject(BitcodeMenuService);

    expect(menu.menu().map((item) => item.id)).toEqual(['inicio']);
  });

  it('tras checkSession() con permisos reales, el menú se recalcula e incluye el grupo autorizado', async () => {
    const sessionId = server.seedSession({
      subject: 'user-1',
      roles: ['Admin'],
      permissions: ['identidad.usuarios.ver'],
    });
    await seedCookieInBrowser(sessionId);

    const session = TestBed.inject(BitcodeSessionService);
    const menu = TestBed.inject(BitcodeMenuService);

    expect(menu.menu().map((item) => item.id)).toEqual(['inicio']);

    await session.checkSession();

    expect(menu.menu().map((item) => item.id)).toEqual(['inicio', 'identidad']);
    expect(menu.menu()[1].children?.map((child) => child.id)).toEqual(['identidad-usuarios']);
  });

  it('logout (markAnonymous) oculta de nuevo los items que requerían permiso', async () => {
    const sessionId = server.seedSession({
      subject: 'user-2',
      roles: ['Admin'],
      permissions: ['identidad.usuarios.ver'],
    });
    await seedCookieInBrowser(sessionId);

    const session = TestBed.inject(BitcodeSessionService);
    const menu = TestBed.inject(BitcodeMenuService);
    await session.checkSession();
    expect(menu.menu().map((item) => item.id)).toEqual(['inicio', 'identidad']);

    session.markAnonymous();

    expect(menu.menu().map((item) => item.id)).toEqual(['inicio']);
  });

  it('no hay parpadeo a menú anónimo mientras se reconfirma la misma sesión ya conocida', async () => {
    const sessionId = server.seedSession({
      subject: 'user-3',
      roles: ['Admin'],
      permissions: ['identidad.usuarios.ver'],
    });
    await seedCookieInBrowser(sessionId);

    const session = TestBed.inject(BitcodeSessionService);
    const menu = TestBed.inject(BitcodeMenuService);
    await session.checkSession();
    const menuAntesDeReconfirmar = menu.menu();
    expect(menuAntesDeReconfirmar.map((item) => item.id)).toEqual(['inicio', 'identidad']);

    // `checkSession()` marca `status` como 'loading' de inmediato (síncrono), pero como la sesión
    // reconfirmada es la MISMA (mismos claims), `BitcodeSessionService.claims()` conserva la misma
    // referencia mientras dura la resolución -- por eso el menú, derivado únicamente de `claims()`, no
    // debe caer transitoriamente a sólo `['inicio']` en este punto intermedio.
    const pending = session.checkSession();
    expect(menu.menu().map((item) => item.id)).toEqual(['inicio', 'identidad']);
    expect(menu.menu()).toBe(menuAntesDeReconfirmar);

    await pending;
    expect(menu.menu().map((item) => item.id)).toEqual(['inicio', 'identidad']);
  });
});
