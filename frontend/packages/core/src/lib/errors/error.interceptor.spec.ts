// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58230/"}
import { HttpClient, HttpErrorResponse, HttpInterceptorFn, provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { catchError, throwError } from 'rxjs';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { ProblemDetailsTestServer } from '../testing/problem-details-test-server';
import { BitcodeHttpError } from './bitcode-http-error';
import { bitcodeErrorInterceptor } from './error.interceptor';

const PORT = 58230;
const BASE_URL = `http://127.0.0.1:${PORT}`;

/**
 * Doble mínimo de `bitcodeAuthInterceptor` (`@bitcode/auth`) -- reproduce únicamente la parte relevante
 * para este test (reacciona a un 401 con un efecto observable) sin introducir una dependencia real de
 * `@bitcode/core` hacia `@bitcode/auth` (sería la dirección de dependencia incorrecta). Sirve para
 * verificar el orden de interceptors documentado en `error.interceptor.ts`: el interceptor "de auth" debe
 * seguir recibiendo el `HttpErrorResponse` crudo, nunca un `BitcodeHttpError` ya envuelto.
 */
function createAuthLikeInterceptor(onUnauthorized: () => void): HttpInterceptorFn {
  return (req, next) =>
    next(req).pipe(
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          onUnauthorized();
        }
        return throwError(() => error);
      }),
    );
}

describe('bitcodeErrorInterceptor (contra un servidor HTTP real de ProblemDetails)', () => {
  let server: ProblemDetailsTestServer;

  beforeAll(async () => {
    server = new ProblemDetailsTestServer();
    await server.start(PORT);
  });

  afterAll(async () => {
    await server.stop();
  });

  it('no interfiere con una respuesta exitosa', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr(), withInterceptors([bitcodeErrorInterceptor]))],
    });

    const http = TestBed.inject(HttpClient);
    const response = await firstValueFrom(http.get<{ ok: boolean }>(`${BASE_URL}/ok`));

    expect(response.ok).toBe(true);
  });

  it('envuelve un 400 de ValidationProblemDetails en un BitcodeHttpError con fieldErrors mapeados', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr(), withInterceptors([bitcodeErrorInterceptor]))],
    });

    const http = TestBed.inject(HttpClient);

    await expect(firstValueFrom(http.post(`${BASE_URL}/validation`, {}))).rejects.toSatisfy((error: unknown) => {
      expect(error).toBeInstanceOf(BitcodeHttpError);
      const bitcodeError = error as BitcodeHttpError;
      expect(bitcodeError.uiError.kind).toBe('validation');
      expect(bitcodeError.uiError.fieldErrors).toEqual({
        'Producto.NombreRequerido': ['El nombre es requerido.'],
      });
      expect(bitcodeError.original).toBeInstanceOf(HttpErrorResponse);
      expect(bitcodeError.original.status).toBe(400);
      return true;
    });
  });

  it('envuelve un 404 de negocio conservando el detail como technicalDetail, no como userMessage', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr(), withInterceptors([bitcodeErrorInterceptor]))],
    });

    const http = TestBed.inject(HttpClient);

    await expect(firstValueFrom(http.get(`${BASE_URL}/not-found`))).rejects.toSatisfy((error: unknown) => {
      const bitcodeError = error as BitcodeHttpError;
      expect(bitcodeError.uiError.kind).toBe('not-found');
      expect(bitcodeError.uiError.technicalDetail).toBe('No se encontró el producto con el id solicitado.');
      expect(bitcodeError.uiError.userMessage).not.toBe(bitcodeError.uiError.technicalDetail);
      return true;
    });
  });

  it('extrae el traceId de un 500 no controlado como correlationId', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr(), withInterceptors([bitcodeErrorInterceptor]))],
    });

    const http = TestBed.inject(HttpClient);

    await expect(firstValueFrom(http.get(`${BASE_URL}/unhandled`))).rejects.toSatisfy((error: unknown) => {
      const bitcodeError = error as BitcodeHttpError;
      expect(bitcodeError.uiError.kind).toBe('server');
      // El servidor de prueba reproduce la forma real de GlobalExceptionHandler: `traceId` es un campo
      // del cuerpo (no un header traceparent), así que se toma literal, sin volver a parsearlo como W3C
      // Trace Context (esa parte se ejercita en error-experience.service.spec.ts, vía el header).
      expect(bitcodeError.uiError.correlationId).toBe('00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01');
      return true;
    });
  });

  it('degrada sin romper un 502 con body no-JSON (proxy intermedio)', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr(), withInterceptors([bitcodeErrorInterceptor]))],
    });

    const http = TestBed.inject(HttpClient);

    await expect(firstValueFrom(http.get(`${BASE_URL}/non-json-error`))).rejects.toSatisfy((error: unknown) => {
      const bitcodeError = error as BitcodeHttpError;
      expect(bitcodeError.uiError.kind).toBe('server');
      expect(bitcodeError.uiError.problemDetails).toBeUndefined();
      return true;
    });
  });

  it('degrada sin romper un error sin body en absoluto', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr(), withInterceptors([bitcodeErrorInterceptor]))],
    });

    const http = TestBed.inject(HttpClient);

    await expect(firstValueFrom(http.get(`${BASE_URL}/empty-error`))).rejects.toBeInstanceOf(BitcodeHttpError);
  });

  it('orden recomendado [error, auth]: el interceptor "de auth" sigue viendo el HttpErrorResponse crudo de un 401, y la app recibe el error ya enriquecido', async () => {
    let authSawRawHttpErrorResponse = false;
    const authLike = createAuthLikeInterceptor(() => {
      authSawRawHttpErrorResponse = true;
    });

    // Orden documentado en error.interceptor.ts: [bitcodeErrorInterceptor, authLike] -- authLike es el
    // más interno (ve el HttpErrorResponse crudo primero), bitcodeErrorInterceptor es el más externo (lo
    // que finalmente recibe la aplicación).
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr(), withInterceptors([bitcodeErrorInterceptor, authLike]))],
    });

    const http = TestBed.inject(HttpClient);

    await expect(firstValueFrom(http.get(`${BASE_URL}/unauthorized`))).rejects.toSatisfy((error: unknown) => {
      expect(error).toBeInstanceOf(BitcodeHttpError);
      const bitcodeError = error as BitcodeHttpError;
      expect(bitcodeError.uiError.kind).toBe('unauthorized');
      return true;
    });

    // El interceptor "de auth", más interno en esta cadena, procesó el HttpErrorResponse crudo (nunca un
    // BitcodeHttpError ya envuelto) -- coexistencia sin duplicar ni romper su propia reacción al 401.
    expect(authSawRawHttpErrorResponse).toBe(true);
  });
});
