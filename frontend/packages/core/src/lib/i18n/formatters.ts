import { BitcodeLocale } from './locale.model';

/**
 * Funciones puras de formateo (F7-11), envolviendo `Intl` -- deliberadamente sin estado ni DI, para que
 * `BitcodeLocaleService`/los pipes las reutilicen y también sean testeables/usables fuera de un contexto
 * de inyección de Angular (p. ej. en un `.spec.ts` plano).
 *
 * Ninguna usa el locale/timezone del navegador implícitamente salvo que se lo pase explícito -- evita el
 * error clásico de "funciona en mi máquina" por depender de la configuración regional del entorno de CI.
 */

export interface BitcodeDateFormatOptions {
  readonly timeZone?: string;
  readonly dateStyle?: 'full' | 'long' | 'medium' | 'short';
}

export interface BitcodeDateTimeFormatOptions extends BitcodeDateFormatOptions {
  readonly timeStyle?: 'full' | 'long' | 'medium' | 'short';
}

function toDate(value: Date | string | number): Date {
  return value instanceof Date ? value : new Date(value);
}

export function formatBitcodeDate(
  value: Date | string | number,
  locale: BitcodeLocale,
  options: BitcodeDateFormatOptions = {},
): string {
  if (options.dateStyle) {
    return new Intl.DateTimeFormat(locale, { dateStyle: options.dateStyle, timeZone: options.timeZone }).format(
      toDate(value),
    );
  }
  // Sin `dateStyle` explícito: día/mes/año numéricos con año de 4 dígitos -- `dateStyle: 'short'` de
  // `Intl` trunca el año a 2 dígitos en varios locales (`15/3/26`), ambiguo para datos de negocio.
  return new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'numeric',
    year: 'numeric',
    timeZone: options.timeZone,
  }).format(toDate(value));
}

export function formatBitcodeDateTime(
  value: Date | string | number,
  locale: BitcodeLocale,
  options: BitcodeDateTimeFormatOptions = {},
): string {
  if (options.dateStyle || options.timeStyle) {
    return new Intl.DateTimeFormat(locale, {
      dateStyle: options.dateStyle ?? 'short',
      timeStyle: options.timeStyle ?? 'short',
      timeZone: options.timeZone,
    }).format(toDate(value));
  }
  return new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: 'numeric',
    timeZone: options.timeZone,
  }).format(toDate(value));
}

export interface BitcodeCurrencyFormatOptions {
  /** Código ISO 4217 (p. ej. `ARS`, `USD`). Obligatorio -- a diferencia de `Intl.NumberFormat`, no hay un
   * default razonable: la moneda de un monto es un dato de negocio, no de presentación, y debe venir
   * siempre del backend (nunca inferirse del locale de la UI). */
  readonly currency: string;
}

export function formatBitcodeCurrency(
  value: number,
  locale: BitcodeLocale,
  options: BitcodeCurrencyFormatOptions,
): string {
  return new Intl.NumberFormat(locale, { style: 'currency', currency: options.currency }).format(value);
}

export interface BitcodeNumberFormatOptions {
  readonly minimumFractionDigits?: number;
  readonly maximumFractionDigits?: number;
}

export function formatBitcodeNumber(
  value: number,
  locale: BitcodeLocale,
  options: BitcodeNumberFormatOptions = {},
): string {
  return new Intl.NumberFormat(locale, options).format(value);
}
