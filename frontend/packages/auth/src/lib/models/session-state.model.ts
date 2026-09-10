import { BitcodeUserClaims } from './user-claims.model';

/**
 * Estado de la sesión del lado cliente. Deliberadamente NO incluye ningún token: lo único que persiste
 * en el navegador es la cookie HttpOnly de sesión del BFF (invisible para JavaScript, gestionada por el
 * propio navegador). Este estado es una proyección en memoria de "¿hay sesión?" derivada de la última
 * respuesta del backend -- se pierde en cada recarga completa de página y se vuelve a resolver llamando
 * a `BitcodeSessionService.checkSession()`, nunca se rehidrata desde `localStorage`/`sessionStorage`.
 */
export type BitcodeSessionStatus = 'unknown' | 'loading' | 'authenticated' | 'anonymous';

export interface BitcodeSessionState {
  readonly status: BitcodeSessionStatus;
  readonly claims: BitcodeUserClaims | null;
}

export const INITIAL_SESSION_STATE: BitcodeSessionState = {
  status: 'unknown',
  claims: null,
};
