import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, tap, throwError } from 'rxjs';
import { BitcodeErrorExperienceService } from '../errors/error-experience.service';
import { generateTraceparent } from './correlation';
import { BitcodeTelemetryService } from './telemetry.service';

/**
 * Interceptor de F7-13: agrega un `traceparent` (W3C, ver `correlation.ts`) a cada request saliente que no
 * lo tenga ya, y registra un evento `'trace'` con la duración real (medida con `performance.now()`, no
 * estimada) al completarse -- éxito o error.
 *
 * **Orden recomendado** (mismo patrón documentado en `docs/guia-frontend-errores.md` para
 * `bitcodeErrorInterceptor`): registrar ANTES que `bitcodeErrorInterceptor` en el array de
 * `withInterceptors([...])`, para que el `traceparent` ya esté en el request cuando `bitcodeErrorInterceptor`
 * (más interno en la ida) lo procese, y para que este interceptor vea el error DESPUÉS de que
 * `bitcodeErrorInterceptor` ya lo clasificó -- evita reclasificar el mismo `HttpErrorResponse` dos veces
 * con lógica distinta:
 *
 * ```ts
 * provideHttpClient(withInterceptors([bitcodeTelemetryInterceptor, bitcodeErrorInterceptor]))
 * ```
 *
 * Nunca swallowea el error ni lo transforma -- lo relanza tal cual llegó (`BitcodeHttpError` si corrió
 * después de `bitcodeErrorInterceptor`, `HttpErrorResponse` crudo si no), para no romper el contrato de
 * ningún otro interceptor de la cadena.
 */
export const bitcodeTelemetryInterceptor: HttpInterceptorFn = (req, next) => {
  const telemetry = inject(BitcodeTelemetryService);
  const errorExperience = inject(BitcodeErrorExperienceService);

  const existingTraceparent = req.headers.get('traceparent');
  const traceparent = existingTraceparent ?? generateTraceparent();
  const tracedReq = existingTraceparent ? req : req.clone({ setHeaders: { traceparent } });
  const startedAt = performance.now();

  return next(tracedReq).pipe(
    tap((event) => {
      if ('status' in event) {
        telemetry.recordTrace(tracedReq.urlWithParams, traceparent, performance.now() - startedAt, {
          method: tracedReq.method,
          status: (event as { status?: number }).status,
        });
      }
    }),
    catchError((error: unknown) => {
      telemetry.recordTrace(tracedReq.urlWithParams, traceparent, performance.now() - startedAt, {
        method: tracedReq.method,
        status: error instanceof HttpErrorResponse ? error.status : undefined,
      });
      if (error instanceof HttpErrorResponse) {
        telemetry.recordError(errorExperience.fromHttpError(error), { url: tracedReq.urlWithParams });
      }
      return throwError(() => error);
    }),
  );
};
