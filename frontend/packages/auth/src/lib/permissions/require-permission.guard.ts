import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { BitcodeAuthService } from '../actions/auth.service';
import { BitcodeSessionService } from '../session/session.service';
import { BITCODE_AUTH_CONFIG } from '../config/auth-config';
import { BitcodePermissionMode, hasRequiredPermissions } from './permission-checks';

/**
 * Guard funcional consciente de permisos (F7-04, RBAC del lado cliente).
 *
 * Requiere sesión activa (delega en `checkSession()`, igual que `bitcodeAuthGuard` de F7-03: si el
 * estado todavía no se conoce, lo resuelve antes de decidir) Y que el actor tenga el/los permiso(s)
 * indicados, según `mode` (ver `hasRequiredPermissions`). Si no hay sesión, se comporta exactamente como
 * `bitcodeAuthGuard` (redirige a login vía `window.location`, ya que sin sesión no hay nada que evaluar
 * todavía). Si HAY sesión pero falta algún permiso requerido, redirige a `config.unauthorizedPath`
 * (navegación interna del Router, no `window.location`) -- deliberadamente una ruta distinta de login,
 * porque "sesión válida sin permiso" y "sin sesión" son situaciones distintas para el usuario.
 *
 * ADVERTENCIA EXPLÍCITA (criterio de aceptación de F7-04, mismo tono que el comentario de
 * `bitcodeAuthGuard`): esto es EXCLUSIVAMENTE una conveniencia de UX que evita que la SPA navegue a una
 * pantalla que de todos modos va a fallar contra el backend. NUNCA reemplaza `[RequirePermission]`/ABAC
 * del lado servidor (`Shared.Infrastructure.Security/Permissions`, `Abac/`, Fase 2). Un actor que
 * manipule el router de la SPA, edite el bundle en el navegador, o llame al backend directamente
 * (`curl`, Postman, un cliente propio) sortea este guard por completo -- por eso cada endpoint protegido
 * debe seguir validando el permiso server-side sin excepción. Ver
 * `require-permission.guard.spec.ts`, caso "un 403 real del backend no depende de este guard".
 */
export function bitcodeRequirePermissionGuard(
  permissions: string | readonly string[],
  options?: { readonly mode?: BitcodePermissionMode },
): CanActivateFn {
  return async (_route, state) => {
    // inject() sólo es válido antes del primer `await` (se apoya en la injection context sincrónica de
    // Angular al invocar el guard, igual que `bitcodeAuthGuard` de F7-03) -- por eso todas las
    // dependencias se resuelven acá arriba, no más abajo en el flujo.
    const session = inject(BitcodeSessionService);
    const config = inject(BITCODE_AUTH_CONFIG);
    const router = inject(Router);
    const auth = inject(BitcodeAuthService);

    const currentStatus = session.status();
    const result =
      currentStatus === 'unknown' || currentStatus === 'loading'
        ? await session.checkSession()
        : { status: currentStatus, claims: session.claims() };

    if (result.status !== 'authenticated') {
      auth.login(state.url);
      return false;
    }

    if (hasRequiredPermissions(result.claims, permissions, options?.mode)) {
      return true;
    }

    return router.parseUrl(config.unauthorizedPath);
  };
}
