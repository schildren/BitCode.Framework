import { Injectable, InjectionToken, computed, inject, signal } from '@angular/core';
import {
  BITCODE_DEFAULT_LOCALE,
  BITCODE_TIME_ZONE,
  BitcodeLocale,
  isBitcodeLocale,
} from './locale.model';

const STORAGE_KEY = 'bitcode.locale';

/**
 * Punto de extensión para dónde persistir el locale elegido por el usuario -- por defecto
 * `window.localStorage`, pero inyectable para tests (sin tocar `localStorage` real) y para apps que no
 * corran en un browser con `localStorage` disponible (SSR). Ninguna dependencia de negocio (saldos,
 * auditoría, etc.) se apoya en esto -- es sólo una preferencia de presentación.
 */
export interface BitcodeLocaleStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
}

export const BITCODE_LOCALE_STORAGE = new InjectionToken<BitcodeLocaleStorage>('BITCODE_LOCALE_STORAGE', {
  factory: () => (typeof window !== 'undefined' ? window.localStorage : new InMemoryLocaleStorage()),
});

class InMemoryLocaleStorage implements BitcodeLocaleStorage {
  private readonly values = new Map<string, string>();
  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }
  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }
}

/**
 * Estado de localización activo de la aplicación (F7-11: "Idiomas, fechas, moneda y zona horaria"). Fuente
 * única de verdad para el locale/timezone que consumen los pipes/formateadores de `@bitcode/core` y,
 * opcionalmente, `BitcodeTranslationService`. Es de presentación exclusivamente -- nunca debe usarse para
 * decidir reglas de negocio (esas viven en el backend).
 */
@Injectable({ providedIn: 'root' })
export class BitcodeLocaleService {
  private readonly storage = inject(BITCODE_LOCALE_STORAGE);
  private readonly defaultTimeZone = inject(BITCODE_TIME_ZONE);

  private readonly localeSignal = signal<BitcodeLocale>(this.readInitialLocale());
  private readonly timeZoneSignal = signal<string>(this.defaultTimeZone);

  readonly locale = this.localeSignal.asReadonly();
  readonly timeZone = this.timeZoneSignal.asReadonly();

  /** `Intl.Locale` derivado del locale activo -- conveniencia para consumidores avanzados
   * (p. ej. `Intl.DisplayNames`) que no necesitan reimplementar el parseo de `es-AR`. */
  readonly intlLocale = computed(() => new Intl.Locale(this.localeSignal()));

  setLocale(locale: BitcodeLocale): void {
    this.localeSignal.set(locale);
    this.storage.setItem(STORAGE_KEY, locale);
  }

  setTimeZone(timeZone: string): void {
    this.timeZoneSignal.set(timeZone);
  }

  private readInitialLocale(): BitcodeLocale {
    const stored = this.storage.getItem(STORAGE_KEY);
    return isBitcodeLocale(stored) ? stored : BITCODE_DEFAULT_LOCALE;
  }
}
