import { Injectable, computed, inject } from '@angular/core';
import { BITCODE_DEFAULT_LOCALE } from './locale.model';
import { BitcodeLocaleService } from './locale.service';
import { BITCODE_TRANSLATIONS } from './translation.model';

/**
 * Traduce claves del catálogo activo (F7-11) según `BitcodeLocaleService.locale()`. Reactivo: expone
 * `catalog` como `computed`, así que cualquier consumidor basado en signals (o el pipe `bcT`) se actualiza
 * solo cuando el usuario cambia de idioma, sin recargar la página.
 *
 * Fallback en cascada, nunca lanza por una clave faltante (evita que un catálogo incompleto rompa la UI):
 * locale activo -> `BITCODE_DEFAULT_LOCALE` -> la clave misma (visible como texto plano, para detectar en
 * QA claves sin traducir sin dejar la UI en blanco).
 */
@Injectable({ providedIn: 'root' })
export class BitcodeTranslationService {
  private readonly localeService = inject(BitcodeLocaleService);
  private readonly translations = inject(BITCODE_TRANSLATIONS);

  readonly catalog = computed(() => this.translations[this.localeService.locale()] ?? {});

  translate(key: string, params?: Readonly<Record<string, string | number>>): string {
    const template =
      this.catalog()[key] ?? this.translations[BITCODE_DEFAULT_LOCALE]?.[key] ?? key;
    return params ? this.interpolate(template, params) : template;
  }

  /** Interpolación simple `{{param}}` -- suficiente para el alcance de F7-11 (sin pluralización ni ICU
   * MessageFormat completo, que quedaría sobredimensionado para el catálogo actual). */
  private interpolate(template: string, params: Readonly<Record<string, string | number>>): string {
    return template.replace(/\{\{(\w+)\}\}/g, (match, key: string) =>
      key in params ? String(params[key]) : match,
    );
  }
}
