import { InjectionToken, Provider } from '@angular/core';
import { BitcodeLocale } from '../i18n/locale.model';
import { ProblemDetails } from './problem-details.model';

/**
 * Clasificación estable de un error HTTP, independiente del `status` numérico exacto -- es lo que
 * permite que el "catálogo de mensajes" (criterio de aceptación "mensajes consistentes") mapee siempre el
 * MISMO tipo de error al mismo texto, sin que cada componente decida el suyo.
 */
export type BitcodeErrorKind =
  | 'validation'
  | 'unauthorized'
  | 'forbidden'
  | 'not-found'
  | 'conflict'
  | 'server'
  | 'network'
  | 'unknown';

/**
 * Resultado de traducir un error HTTP crudo a algo mostrable ("error experience", el entregable de
 * F7-06). Separa deliberadamente:
 *
 * - `userMessage`: SIEMPRE seguro de mostrar tal cual a un usuario final -- nunca el `detail` crudo del
 *   backend (puede contener información técnica interna, p. ej. un mensaje de excepción). Viene del
 *   catálogo de mensajes por `kind` (`BITCODE_ERROR_MESSAGES`), no del backend.
 * - `technicalDetail`/`correlationId`: pensados para un detalle técnico expandible/colapsable (p. ej. "Ver
 *   detalles" en un toast/diálogo de error) que un usuario pueda copiar y reportar a soporte -- ahí sí es
 *   apropiado mostrar el `detail`/`title` crudos del backend.
 */
export interface BitcodeUiError {
  readonly kind: BitcodeErrorKind;
  readonly httpStatus?: number;
  readonly userMessage: string;
  readonly technicalDetail?: string;
  /**
   * Identificador para correlacionar este error de UI con los logs/trazas del backend. Ver la nota
   * honesta en `problem-details.model.ts`: hoy sólo viene poblado para un 500 no controlado (`traceId` de
   * `GlobalExceptionHandler`); en cualquier otro caso queda `undefined` porque el backend no expone hoy
   * ningún correlation id verificable para esas respuestas. SIEMPRE debe mostrarse igual (aunque sea
   * "no disponible") en la experiencia de error, nunca omitirse en silencio sólo porque hoy suele faltar.
   */
  readonly correlationId?: string;
  /** Errores de campo (`ValidationProblemDetails.errors`), sólo para `kind === 'validation'`. */
  readonly fieldErrors?: Readonly<Record<string, readonly string[]>>;
  /** `ProblemDetails` original, cuando pudo parsearse -- para consumo avanzado (p. ej. loguear el
   * `type`/`instance` completos), nunca para construir el mensaje visible directamente. */
  readonly problemDetails?: ProblemDetails;
}

/** Catálogo de mensajes consistentes por `BitcodeErrorKind` -- un único lugar para el texto, en vez de que
 * cada componente que consuma un error decida el suyo. Configurable/localizable (ver
 * `provideBitcodeErrorMessages`/`provideBitcodeErrorMessagesForLocale`, F7-11). */
export type BitcodeErrorMessages = Readonly<Record<BitcodeErrorKind, string>>;

export const DEFAULT_BITCODE_ERROR_MESSAGES: BitcodeErrorMessages = {
  validation: 'Revisá los datos ingresados: hay campos con información inválida.',
  unauthorized: 'Tu sesión no es válida o expiró. Iniciá sesión nuevamente.',
  forbidden: 'No tenés permisos suficientes para realizar esta acción.',
  'not-found': 'No se encontró el recurso solicitado.',
  conflict: 'La operación no pudo completarse por un conflicto con el estado actual de los datos.',
  server: 'Ocurrió un error inesperado. Si el problema persiste, contactá a soporte con el código de correlación.',
  network: 'No se pudo conectar con el servidor. Verificá tu conexión e intentá nuevamente.',
  unknown: 'Ocurrió un error inesperado.',
};

export const BITCODE_ERROR_MESSAGES = new InjectionToken<BitcodeErrorMessages>('BITCODE_ERROR_MESSAGES', {
  factory: () => DEFAULT_BITCODE_ERROR_MESSAGES,
});

/** Registra el catálogo de mensajes de `@bitcode/core` en el árbol de providers de la aplicación
 * (`app.config.ts`). Cualquier `kind` no provisto conserva su mensaje por defecto. */
export function provideBitcodeErrorMessages(overrides: Partial<BitcodeErrorMessages> = {}): Provider[] {
  return [{ provide: BITCODE_ERROR_MESSAGES, useValue: { ...DEFAULT_BITCODE_ERROR_MESSAGES, ...overrides } }];
}

/**
 * Catálogo de `BitcodeErrorMessages` por locale (F7-11) -- cierra el pendiente explícito que dejó F7-06
 * ("sin i18n real de los mensajes del catálogo"). `en-US` es una traducción literal del catálogo por
 * defecto en español; cualquier locale no listado cae al de `es-AR`.
 */
export const BITCODE_ERROR_MESSAGES_BY_LOCALE: Readonly<Record<BitcodeLocale, BitcodeErrorMessages>> = {
  'es-AR': DEFAULT_BITCODE_ERROR_MESSAGES,
  'en-US': {
    validation: 'Check the entered data: some fields contain invalid information.',
    unauthorized: 'Your session is invalid or has expired. Please sign in again.',
    forbidden: 'You do not have sufficient permissions to perform this action.',
    'not-found': 'The requested resource was not found.',
    conflict: 'The operation could not be completed due to a conflict with the current state of the data.',
    server: 'An unexpected error occurred. If the problem persists, contact support with the correlation code.',
    network: 'Could not connect to the server. Check your connection and try again.',
    unknown: 'An unexpected error occurred.',
  },
};

/**
 * Variante de `provideBitcodeErrorMessages` que arranca del catálogo del `locale` indicado en vez de
 * siempre `es-AR`. Sólo aplica en el momento del bootstrap de la app (el token `BITCODE_ERROR_MESSAGES` no
 * es reactivo -- ver `BitcodeLocaleService`/`BitcodeTranslationService` para textos que sí deban
 * recalcularse al cambiar de idioma en caliente, p. ej. vía el catálogo `BITCODE_TRANSLATIONS`).
 */
export function provideBitcodeErrorMessagesForLocale(
  locale: BitcodeLocale,
  overrides: Partial<BitcodeErrorMessages> = {},
): Provider[] {
  const base = BITCODE_ERROR_MESSAGES_BY_LOCALE[locale] ?? DEFAULT_BITCODE_ERROR_MESSAGES;
  return [{ provide: BITCODE_ERROR_MESSAGES, useValue: { ...base, ...overrides } }];
}
