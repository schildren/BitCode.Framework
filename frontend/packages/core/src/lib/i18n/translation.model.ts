import { InjectionToken, Provider } from '@angular/core';
import { BitcodeLocale } from './locale.model';

/** Catálogo plano `clave -> texto` para un único locale. Claves con puntos (`common.save`) por convención
 * de namespacing, sin anidamiento real (más simple de mergear con overrides parciales). */
export type BitcodeTranslationCatalog = Readonly<Record<string, string>>;

export type BitcodeTranslations = Readonly<Record<BitcodeLocale, BitcodeTranslationCatalog>>;

/**
 * Catálogo por defecto de `@bitcode/core`: sólo las claves de uso transversal (acciones comunes de
 * grilla/forms/documents/workflow ya existentes como texto fijo en español) -- NO es un catálogo completo
 * de toda la UI de una aplicación consumidora, que debe extenderlo con `provideBitcodeTranslations`
 * (mergea por clave, no reemplaza el catálogo entero).
 */
export const DEFAULT_BITCODE_TRANSLATIONS: BitcodeTranslations = {
  'es-AR': {
    'common.save': 'Guardar',
    'common.cancel': 'Cancelar',
    'common.confirm': 'Confirmar',
    'common.delete': 'Eliminar',
    'common.close': 'Cerrar',
    'common.loading': 'Cargando…',
    'common.retry': 'Reintentar',
    'common.search': 'Buscar',
    'common.noResults': 'No se encontraron resultados.',
  },
  'en-US': {
    'common.save': 'Save',
    'common.cancel': 'Cancel',
    'common.confirm': 'Confirm',
    'common.delete': 'Delete',
    'common.close': 'Close',
    'common.loading': 'Loading…',
    'common.retry': 'Retry',
    'common.search': 'Search',
    'common.noResults': 'No results found.',
  },
};

export const BITCODE_TRANSLATIONS = new InjectionToken<BitcodeTranslations>('BITCODE_TRANSLATIONS', {
  factory: () => DEFAULT_BITCODE_TRANSLATIONS,
});

/**
 * Registra catálogos de traducción adicionales/override en el árbol de providers de la aplicación,
 * mergeando por locale y por clave con `DEFAULT_BITCODE_TRANSLATIONS` -- una app consumidora normalmente
 * llama esto una vez en `app.config.ts` con sus propias claves de negocio, sin perder las de `@bitcode/core`.
 */
export function provideBitcodeTranslations(overrides: Partial<BitcodeTranslations>): Provider[] {
  const merged: Record<string, BitcodeTranslationCatalog> = {};
  for (const locale of Object.keys(DEFAULT_BITCODE_TRANSLATIONS) as BitcodeLocale[]) {
    merged[locale] = { ...DEFAULT_BITCODE_TRANSLATIONS[locale], ...(overrides[locale] ?? {}) };
  }
  // Locales adicionales que el override introduzca y que no estén en el catálogo por defecto (p. ej. si
  // en el futuro se agrega soporte a un locale sin catálogo propio de @bitcode/core todavía).
  for (const locale of Object.keys(overrides)) {
    if (!(locale in merged)) {
      merged[locale] = overrides[locale as BitcodeLocale] ?? {};
    }
  }
  return [{ provide: BITCODE_TRANSLATIONS, useValue: merged as BitcodeTranslations }];
}
