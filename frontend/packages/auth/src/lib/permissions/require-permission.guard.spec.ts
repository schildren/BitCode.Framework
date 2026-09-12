// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58216/"}
import { HttpClient, HttpErrorResponse, provideHttpClient, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter, RouterStateSnapshot, UrlTree } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { BITCODE_AUTH_CONFIG, DEFAULT_BITCODE_AUTH_CONFIG } from '../config/auth-config';
import { BitcodeWindowLike, BITCODE_WINDOW } from '../config/window.token';
import { BffTestDouble } from '../testing/bff-test-double';
import { bitcodeRequirePermissionGuard } from './require-permission.guard';

const PORT = 58216;
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

function runGuard(
  permissions: string | readonly string[],
  options: Parameters<typeof bitcodeRequirePermissionGuard>[1],
  url: string,
): Promise<boolean | UrlTree> {
  const guard = bitcodeRequirePermissionGuard(permissions, options);
  return TestBed.runInInjectionContext(() =>
    Promise.resolve(guard({} as never, { url } as RouterStateSnapshot)),
  ) as Promise<boolean | UrlTree>;
}

describe('bitcodeRequirePermissionGuard (contra un doble real del BFF)', () => {
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
        provideHttpClient(withXhr()),
        provideRouter([]),
        { provide: BITCODE_WINDOW, useValue: fakeWindow },
        {
          provide: BITCODE_AUTH_CONFIG,
          useValue: { ...DEFAULT_BITCODE_AUTH_CONFIG, sessionEndpoint: `${BASE_URL}/bff/session` },
        },
      ],
    });
  });

  it('sin sesión, deniega y redirige a login (igual que bitcodeAuthGuard) -- no a "sin autorización"', async () => {
    const allowed = await runGuard('identidad.usuarios.crear', undefined, '/usuarios/nuevo');

    expect(allowed).toBe(false);
    expect(fakeWindow.assignedUrls).toHaveLength(1);
    expect(new URL(fakeWindow.assignedUrls[0]).pathname).toBe('/auth/login');
  });

  it('con sesión pero sin el permiso requerido, redirige (Router) a unauthorizedPath, sin tocar window.location', async () => {
    const sessionId = server.seedSession({ subject: 'user-1', roles: ['Ventas'], permissions: [] });
    await seedCookieInBrowser(sessionId);

    const result = await runGuard('identidad.usuarios.crear', undefined, '/usuarios/nuevo');

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe(DEFAULT_BITCODE_AUTH_CONFIG.unauthorizedPath);
    expect(fakeWindow.assignedUrls).toHaveLength(0);
  });

  it('con sesión y el permiso requerido, permite el acceso', async () => {
    const sessionId = server.seedSession({
      subject: 'user-2',
      roles: ['Admin'],
      permissions: ['identidad.usuarios.crear'],
    });
    await seedCookieInBrowser(sessionId);

    const allowed = await runGuard('identidad.usuarios.crear', undefined, '/usuarios/nuevo');

    expect(allowed).toBe(true);
  });

  it('con múltiples permisos y mode "all" (default), exige TODOS', async () => {
    const sessionId = server.seedSession({
      subject: 'user-3',
      roles: ['Admin'],
      permissions: ['identidad.usuarios.crear'],
    });
    await seedCookieInBrowser(sessionId);

    const result = await runGuard(
      ['identidad.usuarios.crear', 'identidad.usuarios.roles.asignar'],
      undefined,
      '/usuarios/nuevo',
    );

    expect(result).toBeInstanceOf(UrlTree);
  });

  it('con múltiples permisos y mode "any", alcanza con uno solo', async () => {
    const sessionId = server.seedSession({
      subject: 'user-4',
      roles: ['Admin'],
      permissions: ['identidad.usuarios.crear'],
    });
    await seedCookieInBrowser(sessionId);

    const allowed = await runGuard(
      ['identidad.usuarios.crear', 'identidad.usuarios.roles.asignar'],
      { mode: 'any' },
      '/usuarios/nuevo',
    );

    expect(allowed).toBe(true);
  });

  it(
    'CRÍTICO (criterio de aceptación F7-04): un 403 real del backend no depende de este guard -- ' +
      'llamando al servicio HTTP DIRECTAMENTE (sorteando router/guard, como haría alguien desde la ' +
      'consola del navegador), el servidor sigue rechazando sin el permiso, y el guard "permitiendo" el ' +
      'acceso a la ruta no habría cambiado eso',
    async () => {
      const sessionId = server.seedSession({ subject: 'user-5', roles: ['Ventas'], permissions: [] });
      await seedCookieInBrowser(sessionId);

      // El guard, correctamente, denegaría la navegación (sin el permiso).
      const guardResult = await runGuard('identidad.usuarios.crear', undefined, '/usuarios/nuevo');
      expect(guardResult).toBeInstanceOf(UrlTree);

      // Aun si algo del lado cliente hubiese permitido la navegación de todos modos (bug, DOM
      // manipulado, un cliente propio que ni siquiera usa el Router de Angular), la llamada HTTP real al
      // recurso protegido sigue dependiendo pura y exclusivamente del backend.
      const http = TestBed.inject(HttpClient);
      const call = firstValueFrom(
        http.post(`${BASE_URL}/bff/api/usuarios`, { nombre: 'x' }, { withCredentials: true }),
      );

      await expect(call).rejects.toSatisfy((error: unknown) => {
        expect(error).toBeInstanceOf(HttpErrorResponse);
        return (error as HttpErrorResponse).status === 403;
      });
    },
  );

  it(
    'inverso: con el permiso concedido server-side, el mismo recurso protegido SÍ responde (confirma ' +
      'que el 403 anterior es autorización real, no un endpoint roto)',
    async () => {
      const sessionId = server.seedSession({
        subject: 'user-6',
        roles: ['Admin'],
        permissions: ['identidad.usuarios.crear'],
      });
      await seedCookieInBrowser(sessionId);

      const http = TestBed.inject(HttpClient);
      const response = await firstValueFrom(
        http.post(`${BASE_URL}/bff/api/usuarios`, { nombre: 'x' }, { withCredentials: true }),
      );

      expect(response).toEqual({ id: 'user-creado' });
    },
  );
});
