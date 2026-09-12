import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { BitcodeHttpError } from './bitcode-http-error';
import { BitcodeErrorExperienceService } from './error-experience.service';

/**
 * Interceptor HTTP funcional de `@bitcode/core`: mapea CUALQUIER error HTTP (cualquier código, incluido
 * pero no limitado a 401/403) a un `BitcodeUiError` consistente (`BitcodeErrorExperienceService`) y lo
 * propaga envuelto en `BitcodeHttpError`, sin alterar el `HttpErrorResponse` original (que sigue
 * disponible en `BitcodeHttpError.original`).
 *
 * ## Orden recomendado de interceptors (coexistencia con `@bitcode/auth`)
 *
 * ```ts
 * provideHttpClient(withInterceptors([bitcodeErrorInterceptor, bitcodeAuthInterceptor]))
 * ```
 *
 * En Angular, el ARRAY de `withInterceptors` define el orden de ida (petición): el primer interceptor
 * de la lista es el más "externo" -- ve la petición primero y la respuesta/error ÚLTIMO (después de que
 * todos los interceptors internos ya corrieron). Con el orden de arriba:
 *
 * 1. `bitcodeAuthInterceptor` (más interno, declarado último) es el primero en recibir el
 *    `HttpErrorResponse` crudo de la red. Sigue siendo el único responsable de la reacción específica a
 *    un 401 (marcar la sesión anónima y redirigir a login) -- este interceptor NO duplica esa lógica ni
 *    la reemplaza. `bitcodeAuthInterceptor` relanza el error tal cual (`throwError(() => error)`), sin
 *    envolverlo, así que este interceptor sigue viendo el `HttpErrorResponse` original.
 * 2. `bitcodeErrorInterceptor` (más externo, declarado primero) recibe el error DESPUÉS de que
 *    `bitcodeAuthInterceptor` ya reaccionó, lo mapea a un `BitcodeUiError` (mensaje consistente,
 *    correlation id si está disponible) y lo entrega envuelto a la aplicación -- para CUALQUIER código de
 *    error, incluyendo el 401/403 que `auth` ya procesó. Ningún componente de la aplicación necesita volver
 *    a decidir qué texto mostrar para un error dado: sólo consume `BitcodeHttpError.uiError`.
 *
 * Si el orden se invierte (`[bitcodeAuthInterceptor, bitcodeErrorInterceptor]`), `bitcodeAuthInterceptor`
 * recibiría un `BitcodeHttpError` en vez del `HttpErrorResponse` crudo en su `catchError`, rompiendo su
 * chequeo `error instanceof HttpErrorResponse` -- por eso el orden de arriba es el único soportado y
 * verificado (ver `error.interceptor.spec.ts`).
 */
export const bitcodeErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const errorExperience = inject(BitcodeErrorExperienceService);

  return next(req).pipe(
    catchError((error: unknown) => {
      const uiError = errorExperience.fromHttpError(error);

      if (error instanceof HttpErrorResponse) {
        return throwError(() => new BitcodeHttpError(error, uiError));
      }

      // No es un HttpErrorResponse (excepción de programación en el pipeline) -- se deja pasar tal cual,
      // sin envolverlo en un tipo pensado específicamente para errores HTTP.
      return throwError(() => error);
    }),
  );
};
