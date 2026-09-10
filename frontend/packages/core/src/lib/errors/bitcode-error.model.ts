import { InjectionToken, Provider } from '@angular/core';
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
 * `provideBitcodeErrorMessages`); i18n real (idiomas múltiples) es F7-11, fuera de alcance de F7-06. */
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
