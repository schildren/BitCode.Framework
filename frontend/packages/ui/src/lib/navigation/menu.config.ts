import { InjectionToken, Provider } from '@angular/core';
import { BitcodeMenuItem } from './menu-item.model';

/**
 * Configuración estática del menú de una app concreta (F7-05). `@bitcode/ui` no tiene opinión sobre qué
 * módulos existen -- la app consumidora (p. ej. `apps/shell`) provee su propio árbol de
 * `BitcodeMenuItem[]` vía `provideBitcodeMenuItems(...)` en `app.config.ts`, igual que
 * `provideBitcodeAuthConfig` en `@bitcode/auth`.
 */
export const BITCODE_MENU_ITEMS = new InjectionToken<readonly BitcodeMenuItem[]>('BITCODE_MENU_ITEMS', {
  factory: () => [],
});

/** Registra el árbol de items de menú de la app en el árbol de providers. */
export function provideBitcodeMenuItems(items: readonly BitcodeMenuItem[]): Provider[] {
  return [{ provide: BITCODE_MENU_ITEMS, useValue: items }];
}
