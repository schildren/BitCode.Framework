import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DEFAULT_BITCODE_ERROR_MESSAGES } from './bitcode-error.model';
import { BitcodeErrorExperienceService } from './error-experience.service';

function buildService(): BitcodeErrorExperienceService {
  TestBed.configureTestingModule({});
  return TestBed.inject(BitcodeErrorExperienceService);
}

describe('BitcodeErrorExperienceService', () => {
  it('clasifica un status 0 (sin ciclo HTTP completo) como error de red, sin intentar parsear el body', () => {
    const service = buildService();

    const uiError = service.fromHttpError(
      new HttpErrorResponse({ status: 0, statusText: 'Unknown Error', error: new ProgressEvent('error') }),
    );

    expect(uiError.kind).toBe('network');
    expect(uiError.userMessage).toBe(DEFAULT_BITCODE_ERROR_MESSAGES.network);
    expect(uiError.problemDetails).toBeUndefined();
  });

  it('mapea un ValidationProblemDetails (400) a kind "validation" con fieldErrors, sin mostrar el detail crudo como mensaje', () => {
    const service = buildService();

    const uiError = service.fromHttpError(
      new HttpErrorResponse({
        status: 400,
        error: {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { 'Producto.NombreRequerido': ['El nombre es requerido.'] },
        },
      }),
    );

    expect(uiError.kind).toBe('validation');
    expect(uiError.userMessage).toBe(DEFAULT_BITCODE_ERROR_MESSAGES.validation);
    expect(uiError.fieldErrors).toEqual({ 'Producto.NombreRequerido': ['El nombre es requerido.'] });
    expect(uiError.httpStatus).toBe(400);
  });

  it('mapea 401/403/404/409 a su kind correspondiente con el mensaje consistente del catálogo', () => {
    const service = buildService();

    const casos: ReadonlyArray<[number, string]> = [
      [401, 'unauthorized'],
      [403, 'forbidden'],
      [404, 'not-found'],
      [409, 'conflict'],
    ];

    for (const [status, kind] of casos) {
      const uiError = service.fromHttpError(
        new HttpErrorResponse({ status, error: { title: `Codigo.${status}`, status, detail: `detalle-${status}` } }),
      );
      expect(uiError.kind).toBe(kind);
      expect(uiError.userMessage).toBe(DEFAULT_BITCODE_ERROR_MESSAGES[kind as keyof typeof DEFAULT_BITCODE_ERROR_MESSAGES]);
      // El detalle técnico crudo del backend queda reservado a technicalDetail, nunca a userMessage.
      expect(uiError.technicalDetail).toBe(`detalle-${status}`);
      expect(uiError.userMessage).not.toContain(`detalle-${status}`);
    }
  });

  it('extrae el traceId de la forma no estándar de GlobalExceptionHandler (500) como correlationId', () => {
    const service = buildService();

    const uiError = service.fromHttpError(
      new HttpErrorResponse({
        status: 500,
        error: {
          type: 'https://tools.ietf.org/html/rfc7231#section-6.6.1',
          title: 'Ha ocurrido un error inesperado',
          status: 500,
          traceId: '0HN6JQK8L8VQR:00000001',
        },
      }),
    );

    expect(uiError.kind).toBe('server');
    expect(uiError.correlationId).toBe('0HN6JQK8L8VQR:00000001');
  });

  it('extrae el trace-id de un header traceparent (W3C Trace Context) cuando está presente', () => {
    const service = buildService();

    const uiError = service.fromHttpError(
      new HttpErrorResponse({
        status: 500,
        error: { title: 'Error', status: 500 },
        headers: new HttpHeaders({ traceparent: '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01' }),
      }),
    );

    expect(uiError.correlationId).toBe('4bf92f3577b34da6a3ce929d0e0e4736');
  });

  it('sin ningún correlation id disponible (caso real más común hoy), correlationId queda undefined sin fallar', () => {
    const service = buildService();

    const uiError = service.fromHttpError(
      new HttpErrorResponse({ status: 404, error: { title: 'Producto.NoEncontrado', status: 404 } }),
    );

    expect(uiError.correlationId).toBeUndefined();
  });

  it('degrada sin lanzar cuando el body de error no es JSON válido (p. ej. un proxy intermedio)', () => {
    const service = buildService();

    const uiError = service.fromHttpError(
      new HttpErrorResponse({ status: 502, error: '<html><body>502 Bad Gateway</body></html>', statusText: 'Bad Gateway' }),
    );

    expect(uiError.kind).toBe('server');
    expect(uiError.problemDetails).toBeUndefined();
    expect(uiError.userMessage).toBe(DEFAULT_BITCODE_ERROR_MESSAGES.server);
  });

  it('degrada a "unknown" un error que no es HttpErrorResponse, sin lanzar', () => {
    const service = buildService();

    const uiError = service.fromHttpError(new Error('fallo de programación, no de red'));

    expect(uiError.kind).toBe('unknown');
    expect(uiError.userMessage).toBe(DEFAULT_BITCODE_ERROR_MESSAGES.unknown);
    expect(uiError.technicalDetail).toBe('fallo de programación, no de red');
  });
});
