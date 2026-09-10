import { BitcodeUserClaims } from '../models/user-claims.model';

/**
 * Semántica cuando se piden varios permisos a la vez (guard o directiva, F7-04):
 *
 * - `'all'` (default): el actor debe tener TODOS los permisos listados. Es el default deliberado porque
 *   es la opción más restrictiva -- una ruta/elemento protegido por varios permisos casi siempre
 *   representa "necesita poder hacer A Y B", no "alcanza con cualquiera de las dos". Pedir explícitamente
 *   `'any'` es la excepción, no la regla.
 * - `'any'`: alcanza con que el actor tenga AL MENOS UNO de los permisos listados (equivalente a "esta
 *   ruta le sirve a más de un rol/perfil, cualquiera de estos permisos habilita el acceso").
 */
export type BitcodePermissionMode = 'all' | 'any';

/** Normaliza `string | string[]` al array que consumen los helpers de abajo. */
export function toPermissionList(permissions: string | readonly string[]): readonly string[] {
  return typeof permissions === 'string' ? [permissions] : permissions;
}

/**
 * `true` si `claims` (sesión ya resuelta) satisface los permisos requeridos según `mode`.
 *
 * IMPORTANTE (criterio de aceptación de F7-04, "la UI no sustituye validación backend"): esta función es
 * exclusivamente una conveniencia de UX -- decide si MOSTRAR u OCULTAR algo, o si REDIRIGIR una
 * navegación, nunca si una operación es legítima. `claims.permissions` es lo que el backend publicó en
 * la ÚLTIMA resolución de sesión conocida por el cliente: puede estar desactualizado (un permiso
 * revocado server-side después del último `checkSession()`), incompleto (ver nota honesta en
 * `BitcodeUserClaims.permissions`), o directamente ausente si el backend todavía no expone permisos en
 * `/bff/session`. La única fuente de verdad real es la respuesta HTTP del endpoint protegido en sí
 * (401/403 vía `[RequirePermission]`/ABAC del lado servidor) -- ver
 * `guards/require-permission.guard.spec.ts`, caso "un 403 real del backend no depende de este chequeo".
 */
export function hasRequiredPermissions(
  claims: BitcodeUserClaims | null,
  required: string | readonly string[],
  mode: BitcodePermissionMode = 'all',
): boolean {
  const requiredList = toPermissionList(required);
  if (requiredList.length === 0) {
    return true;
  }
  if (!claims) {
    return false;
  }

  const granted = new Set(claims.permissions);
  return mode === 'any'
    ? requiredList.some((permission) => granted.has(permission))
    : requiredList.every((permission) => granted.has(permission));
}
