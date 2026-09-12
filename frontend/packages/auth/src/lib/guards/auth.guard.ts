import { CanActivateFn } from '@angular/router';
import { inject } from '@angular/core';
import { BitcodeAuthService } from '../actions/auth.service';
import { BitcodeSessionService } from '../session/session.service';

/**
 * Guard funcional que exige una sesión activa antes de permitir navegar a una ruta protegida.
 *
 * Esto es exclusivamente una conveniencia de UX del lado cliente (evita que la SPA muestre una ruta
 * "vacía" a un usuario sin sesión antes de que la primera llamada HTTP falle con 401): NUNCA reemplaza
 * la autorización real del backend, que sigue validando la sesión/token en cada request. F7-04 agrega
 * guards conscientes de roles/permisos (RBAC/ABAC) sobre esta misma base -- este guard de F7-03 sólo
 * resuelve "¿hay sesión sí/no?".
 */
export const bitcodeAuthGuard: CanActivateFn = async (_route, state) => {
  const session = inject(BitcodeSessionService);
  const auth = inject(BitcodeAuthService);

  const currentStatus = session.status();
  const result =
    currentStatus === 'unknown' || currentStatus === 'loading'
      ? await session.checkSession()
      : { status: currentStatus, claims: session.claims() };

  if (result.status === 'authenticated') {
    return true;
  }

  auth.login(state.url);
  return false;
};
