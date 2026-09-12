import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { bitcodeAuthInterceptor, provideBitcodeAuthConfig } from '@bitcode/auth';
import { provideBitcodeMenuItems } from '@bitcode/ui';
import { appRoutes } from './app.routes';
import { SHELL_MENU_ITEMS } from './navigation/shell-menu.config';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(appRoutes),
    // F7-03/F7-04: sesión BFF/OIDC vía cookie HttpOnly, sin tokens en el cliente. Ver
    // docs/guia-frontend-auth.md -- `sessionEndpoint` sigue siendo un contrato PROPUESTO (`/bff/session`)
    // hasta que un host BFF real lo exponga.
    provideBitcodeAuthConfig(),
    provideHttpClient(withInterceptors([bitcodeAuthInterceptor])),
    // F7-05: árbol de navegación concreto de esta app (ver navigation/shell-menu.config.ts), consumido de
    // forma genérica y filtrado por permisos por `BitcodeMenuService` (`@bitcode/ui`).
    provideBitcodeMenuItems(SHELL_MENU_ITEMS),
  ],
};
