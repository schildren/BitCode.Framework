import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { BITCODE_AUTH_CONFIG } from '../config/auth-config';
import { BitcodeAuthService } from '../actions/auth.service';
import { BitcodeSessionService } from '../session/session.service';

/**
 * Interceptor HTTP funcional de `@bitcode/auth`:
 *
 * 1. Fuerza `withCredentials: true` en toda request saliente, para que la cookie HttpOnly de sesión del
 *    BFF (`bc-bff-session`) viaje siempre que corresponda -- sin que cada llamador tenga que acordarse
 *    de configurarlo por su cuenta.
 * 2. Centraliza el manejo de un 401 (sesión expirada/inválida/ausente): actualiza el estado en memoria
 *    (`BitcodeSessionService.markAnonymous`) y redirige al login, salvo que la request que falló sea la
 *    de resolución de sesión (`sessionEndpoint`) o los propios endpoints de login/logout -- para esos,
 *    un 401 es un resultado normal ("todavía no hay sesión"/"la sesión ya no existía"), no un evento que
 *    deba disparar una redirección adicional (evitaría un ciclo de redirecciones en la carga inicial de
 *    una página pública).
 */
export const bitcodeAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const config = inject(BITCODE_AUTH_CONFIG);
  const session = inject(BitcodeSessionService);
  const auth = inject(BitcodeAuthService);

  const authEndpoints: readonly string[] = [config.sessionEndpoint, config.logoutEndpoint, config.logoutAllEndpoint];
  const isAuthEndpointRequest = authEndpoints.some(
    (endpoint) => req.url === endpoint || req.url.startsWith(endpoint),
  );

  const requestWithCredentials = req.clone({ withCredentials: true });

  return next(requestWithCredentials).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        session.markAnonymous();
        if (!isAuthEndpointRequest) {
          auth.login();
        }
      }
      return throwError(() => error);
    }),
  );
};
