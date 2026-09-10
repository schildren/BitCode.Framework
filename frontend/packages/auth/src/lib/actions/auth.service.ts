import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BITCODE_AUTH_CONFIG } from '../config/auth-config';
import { BITCODE_WINDOW } from '../config/window.token';
import { BitcodeSessionService } from '../session/session.service';

interface LogoutResponse {
  readonly idpEndSessionUri?: string;
}

/**
 * Login/logout de `@bitcode/auth`. Ningún método de esta clase implementa un flujo OIDC en JavaScript:
 * `login` únicamente reubica el navegador hacia el endpoint de login del BFF (`GET /auth/login`,
 * navegación completa) y `logout`/`logoutAllDevices` invocan los endpoints POST del BFF documentados en
 * `docs/guia-oidc-adapter.md` (F2-03/F2-06) -- el `code`/PKCE, el intercambio por tokens y la sesión
 * server-side son responsabilidad exclusiva del backend.
 */
@Injectable({ providedIn: 'root' })
export class BitcodeAuthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(BITCODE_AUTH_CONFIG);
  private readonly session = inject(BitcodeSessionService);
  private readonly windowRef = inject(BITCODE_WINDOW);

  /** Construye la URL absoluta de login, incluyendo `returnUrl` si se provee. */
  buildLoginUrl(returnUrl?: string): string {
    const url = new URL(this.config.loginPath, this.windowRef.location.origin);
    if (returnUrl) {
      url.searchParams.set(this.config.returnUrlParam, returnUrl);
    }
    return url.toString();
  }

  /**
   * Redirige el navegador (navegación completa, nunca `fetch`) al login del BFF. Por defecto vuelve a
   * la ruta actual tras el login.
   */
  login(returnUrl: string = this.windowRef.location.pathname + this.windowRef.location.search): void {
    this.windowRef.location.assign(this.buildLoginUrl(returnUrl));
  }

  /**
   * Cierra la sesión actual (`POST logoutEndpoint`, F2-03). Limpia el estado en memoria del cliente
   * incondicionalmente (incluso si la llamada falla, p. ej. porque la sesión ya había expirado) y, si el
   * IdP publica `end_session_endpoint` (RP-Initiated Logout), navega allí para cerrar también la sesión
   * SSO -- en caso contrario, navega a `postLogoutRedirectPath` si se indicó uno.
   */
  async logout(options: { readonly postLogoutRedirectPath?: string } = {}): Promise<void> {
    let idpEndSessionUri: string | undefined;
    try {
      const response = await firstValueFrom(
        this.http.post<LogoutResponse>(this.config.logoutEndpoint, null, { withCredentials: true }),
      );
      idpEndSessionUri = response?.idpEndSessionUri;
    } catch {
      // Best-effort: si la sesión ya había expirado/sido revocada del lado del servidor, el POST puede
      // fallar (401/404) -- igual limpiamos el estado local, que es la única fuente de verdad que este
      // paquete controla del lado cliente.
    } finally {
      this.session.clear();
    }

    if (idpEndSessionUri) {
      this.windowRef.location.assign(idpEndSessionUri);
      return;
    }

    if (options.postLogoutRedirectPath) {
      this.windowRef.location.assign(options.postLogoutRedirectPath);
    }
  }

  /** Cierra TODAS las sesiones del sujeto autenticado (`POST logoutAllEndpoint`, F2-06). */
  async logoutAllDevices(options: { readonly postLogoutRedirectPath?: string } = {}): Promise<void> {
    try {
      await firstValueFrom(this.http.post(this.config.logoutAllEndpoint, null, { withCredentials: true }));
    } catch {
      // Best-effort, mismo criterio que logout(): limpiamos el estado local aunque la llamada falle.
    } finally {
      this.session.clear();
    }

    if (options.postLogoutRedirectPath) {
      this.windowRef.location.assign(options.postLogoutRedirectPath);
    }
  }
}
