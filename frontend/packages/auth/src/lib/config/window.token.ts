import { InjectionToken } from '@angular/core';

/**
 * Abstracción mínima de `window` para poder reemplazar la navegación real del navegador en pruebas
 * (jsdom no implementa `window.location.assign`/`replace`, y no se debe depender de una navegación real
 * para probar que `@bitcode/auth` decide correctamente CUÁNDO redirigir). El código de producción usa
 * el `window` real vía el factory por defecto.
 */
export interface BitcodeWindowLike {
  readonly location: {
    assign(url: string): void;
    readonly origin: string;
    readonly pathname: string;
    readonly search: string;
  };
}

export const BITCODE_WINDOW = new InjectionToken<BitcodeWindowLike>('BITCODE_WINDOW', {
  factory: () => window,
});
