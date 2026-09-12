import { InjectionToken } from '@angular/core';

/**
 * Locales soportados hoy por `@bitcode/core` (F7-11). BCP-47 completo (idioma + región), no sólo el
 * idioma -- porque el formato de fecha/moneda/número depende de la región tanto como del idioma (p. ej.
 * `es-AR` usa `$` con separador de miles `.` y decimal `,`; `es-MX` no).
 *
 * Agregar un locale nuevo implica: (1) agregarlo acá, (2) agregar su catálogo en
 * `BITCODE_TRANSLATIONS`/`BITCODE_ERROR_MESSAGES_BY_LOCALE` si corresponde texto propio -- si no se agrega
 * catálogo, `BitcodeTranslationService` cae al locale por defecto sin romper.
 */
export type BitcodeLocale = 'es-AR' | 'en-US';

export const BITCODE_DEFAULT_LOCALE: BitcodeLocale = 'es-AR';

export const BITCODE_SUPPORTED_LOCALES: readonly BitcodeLocale[] = ['es-AR', 'en-US'];

export function isBitcodeLocale(value: unknown): value is BitcodeLocale {
  return typeof value === 'string' && (BITCODE_SUPPORTED_LOCALES as readonly string[]).includes(value);
}

/**
 * Zona horaria IANA activa para formateo de fechas/horas (criterio de aceptación "zona horaria"). Por
 * defecto la del navegador (`Intl.DateTimeFormat().resolvedOptions().timeZone`) -- una aplicación puede
 * sobreescribirla (p. ej. con la zona horaria de la organización/tenant que devuelva el backend) con
 * `provideBitcodeTimeZone`.
 */
export const BITCODE_TIME_ZONE = new InjectionToken<string>('BITCODE_TIME_ZONE', {
  factory: () => Intl.DateTimeFormat().resolvedOptions().timeZone,
});

export function provideBitcodeTimeZone(timeZone: string) {
  return { provide: BITCODE_TIME_ZONE, useValue: timeZone };
}
