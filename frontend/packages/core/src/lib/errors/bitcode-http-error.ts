import { HttpErrorResponse } from '@angular/common/http';
import { BitcodeUiError } from './bitcode-error.model';

/**
 * Envoltorio que `bitcodeErrorInterceptor` propaga en lugar de (además de) el `HttpErrorResponse` crudo,
 * para que el código de aplicación pueda consumir directamente un `BitcodeUiError` ya mapeado sin repetir
 * la lógica de parseo/clasificación en cada componente -- ese es el punto central de "mensajes
 * consistentes" (criterio de aceptación de F7-06): un único lugar decide el mapeo, todo el resto sólo lo
 * consume.
 *
 * Extiende `Error` (no reemplaza a `HttpErrorResponse`, lo conserva en `original`) para seguir siendo
 * compatible con cualquier manejo genérico de errores de RxJS/Angular que espere un `Error`.
 */
export class BitcodeHttpError extends Error {
  constructor(
    readonly original: HttpErrorResponse,
    readonly uiError: BitcodeUiError,
  ) {
    super(uiError.userMessage);
    this.name = 'BitcodeHttpError';
  }
}
