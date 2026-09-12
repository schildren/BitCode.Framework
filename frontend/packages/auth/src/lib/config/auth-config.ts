import { InjectionToken, Provider } from '@angular/core';

/**
 * Configuración de `@bitcode/auth` frente al contrato BFF/OIDC documentado en
 * `docs/guia-oidc-adapter.md` (F2-02/F2-03/F2-06).
 *
 * Nota honesta sobre `sessionEndpoint`: `docs/guia-oidc-adapter.md` documenta `/auth/login`,
 * `/auth/logout` (F2-03) y `/auth/logout-all` (F2-06) porque esos son los que el backend implementa
 * hoy, pero NO documenta ningún endpoint "quién soy" (tipo `GET /bff/user`) -- ningún host real del
 * repositorio lo expone todavía. `/bff/session` es el contrato que este paquete PROPONE y contra el que
 * se probó (ver `docs/guia-frontend-auth.md`, sección "Pendiente real"): agregarlo al backend
 * (`Shared.Infrastructure.Web/Security/Bff`) es un pendiente explícito de una tarea futura de backend,
 * no de esta tarea de Fase 7.
 */
export interface BitcodeAuthConfig {
  /**
   * Endpoint que devuelve los claims de la sesión actual (200) o 401 si no hay sesión válida. Nunca se
   * le pide un token: la cookie HttpOnly de sesión viaja sola gracias a `withCredentials`.
   */
  readonly sessionEndpoint: string;
  /**
   * Ruta a la que se redirige el navegador (`window.location`, navegación completa, nunca `fetch`) para
   * iniciar el flujo de login del BFF (`GET /auth/login`, F2-02/F2-03). El intercambio de `code`/PKCE y
   * la creación de la sesión ocurren enteramente server-side; el navegador solo es reubicado.
   */
  readonly loginPath: string;
  /** Endpoint invocado con `fetch`/`HttpClient` (`POST /auth/logout`, F2-03) para cerrar la sesión actual. */
  readonly logoutEndpoint: string;
  /** Endpoint invocado con `fetch`/`HttpClient` (`POST /auth/logout-all`, F2-06) para cerrar todas las
   * sesiones del sujeto autenticado (todos los dispositivos). */
  readonly logoutAllEndpoint: string;
  /** Nombre del parámetro de query string usado para indicarle al BFF a dónde volver tras el login. */
  readonly returnUrlParam: string;
  /**
   * Ruta de la SPA (navegación interna de Angular Router, NO `window.location`) a la que
   * `bitcodeRequirePermissionGuard` (F7-04) redirige cuando hay sesión activa pero el usuario no tiene
   * el/los permiso(s) requeridos por la ruta. Deliberadamente distinta de `loginPath`: esto NO es "sin
   * sesión" (ese caso lo sigue resolviendo `bitcodeAuthGuard` de F7-03), es "sesión válida, permiso
   * insuficiente" -- redirigir a login otra vez sería confuso y no resolvería nada (el usuario ya está
   * autenticado).
   */
  readonly unauthorizedPath: string;
}

export const DEFAULT_BITCODE_AUTH_CONFIG: BitcodeAuthConfig = {
  sessionEndpoint: '/bff/session',
  loginPath: '/auth/login',
  logoutEndpoint: '/auth/logout',
  logoutAllEndpoint: '/auth/logout-all',
  returnUrlParam: 'returnUrl',
  unauthorizedPath: '/unauthorized',
};

export const BITCODE_AUTH_CONFIG = new InjectionToken<BitcodeAuthConfig>('BITCODE_AUTH_CONFIG', {
  factory: () => DEFAULT_BITCODE_AUTH_CONFIG,
});

/** Registra la configuración de `@bitcode/auth` en el árbol de providers de la aplicación (`app.config.ts`).
 * Cualquier campo no provisto conserva su valor por defecto. */
export function provideBitcodeAuthConfig(config: Partial<BitcodeAuthConfig> = {}): Provider[] {
  return [{ provide: BITCODE_AUTH_CONFIG, useValue: { ...DEFAULT_BITCODE_AUTH_CONFIG, ...config } }];
}
