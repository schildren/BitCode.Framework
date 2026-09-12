import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { BITCODE_ERROR_MESSAGES, BitcodeErrorKind, BitcodeUiError } from './bitcode-error.model';
import {
  isProblemDetails,
  isValidationProblemDetails,
  ProblemDetails,
  ValidationProblemDetails,
} from './problem-details.model';

const TRACEPARENT_HEADER = 'traceparent';
// Nombre propuesto/best-effort: ningún backend del repositorio lo emite todavía (ver nota en
// `problem-details.model.ts`), pero si un proxy/gateway intermedio lo agrega en el futuro, se
// aprovecha sin cambiar este código.
const CORRELATION_ID_HEADER = 'x-correlation-id';

/**
 * Traduce cualquier error HTTP crudo (`HttpErrorResponse` u otra excepción) a un `BitcodeUiError`
 * consistente -- el corazón de la "error experience" de F7-06. Es el ÚNICO lugar del frontend que decide
 * qué mensaje se muestra para cada tipo de error; ningún componente debería reimplementar este mapeo.
 */
@Injectable({ providedIn: 'root' })
export class BitcodeErrorExperienceService {
  private readonly messages = inject(BITCODE_ERROR_MESSAGES);

  fromHttpError(error: unknown): BitcodeUiError {
    if (error instanceof HttpErrorResponse) {
      return this.fromHttpErrorResponse(error);
    }

    // No es un error HTTP (p. ej. una excepción de programación en el propio pipeline de RxJS antes de
    // llegar al backend). Se degrada a "unknown" sin romper: nunca se relanza sin clasificar.
    const message = error instanceof Error ? error.message : undefined;
    return {
      kind: 'unknown',
      userMessage: this.messages.unknown,
      technicalDetail: message,
    };
  }

  private fromHttpErrorResponse(error: HttpErrorResponse): BitcodeUiError {
    // status 0 = el request nunca llegó a completar un ciclo HTTP real (DNS, CORS, conexión rechazada,
    // proxy caído sin responder) -- no es un error que el backend haya podido formatear como
    // ProblemDetails, así que ni se intenta parsear el cuerpo.
    if (error.status === 0) {
      return {
        kind: 'network',
        httpStatus: 0,
        userMessage: this.messages.network,
        technicalDetail: error.message,
      };
    }

    const problemDetails = this.parseProblemDetails(error);
    const kind = this.classify(error.status);
    const correlationId = this.extractCorrelationId(error, problemDetails);

    if (kind === 'validation' && problemDetails && isValidationProblemDetails(problemDetails)) {
      return {
        kind,
        httpStatus: error.status,
        userMessage: this.messages.validation,
        technicalDetail: problemDetails.detail ?? problemDetails.title,
        correlationId,
        fieldErrors: (problemDetails as ValidationProblemDetails).errors,
        problemDetails,
      };
    }

    return {
      kind,
      httpStatus: error.status,
      userMessage: this.messages[kind],
      // El `detail`/`title` del backend NUNCA se usa como `userMessage` (regla de la tarea: puede
      // contener información técnica interna, p. ej. el código de un `Result.Failure` de dominio) --
      // sólo se expone en `technicalDetail`, pensado para un detalle expandible/log, no para el texto
      // principal visible.
      technicalDetail: problemDetails?.detail ?? problemDetails?.title ?? error.message,
      correlationId,
      problemDetails,
    };
  }

  /**
   * Intenta parsear el cuerpo del error como `ProblemDetails`. Degrada a `undefined` sin lanzar en
   * cualquier caso que no lo permita (cuerpo vacío, texto plano, HTML de un proxy intermedio, JSON que no
   * tiene forma de ProblemDetails) -- este es el caso explícito pedido por la tarea: un 502 de un proxy
   * sin body JSON no debe romper el interceptor.
   */
  private parseProblemDetails(error: HttpErrorResponse): ProblemDetails | undefined {
    const body: unknown = error.error;

    if (isProblemDetails(body)) {
      return body;
    }

    // Angular/HttpXhrBackend a veces deja el cuerpo crudo como string cuando el parseo automático de
    // JSON falla (p. ej. si el `Content-Type` no es JSON, o el body está vacío) -- se reintenta acá una
    // vez más antes de rendirse.
    if (typeof body === 'string' && body.trim().length > 0) {
      try {
        const parsed: unknown = JSON.parse(body);
        if (isProblemDetails(parsed)) {
          return parsed;
        }
      } catch {
        // No era JSON válido (p. ej. HTML de un proxy) -- degradación esperada, no un bug.
        return undefined;
      }
    }

    return undefined;
  }

  private classify(status: number): BitcodeErrorKind {
    switch (status) {
      case 400:
        return 'validation';
      case 401:
        return 'unauthorized';
      case 403:
        return 'forbidden';
      case 404:
        return 'not-found';
      case 409:
        return 'conflict';
      default:
        return status >= 500 ? 'server' : 'unknown';
    }
  }

  /**
   * Best-effort: hoy el backend (ver nota en `problem-details.model.ts`) sólo expone `traceId` como
   * propiedad del cuerpo, y únicamente en la forma no estándar de `GlobalExceptionHandler` (errores 500
   * no controlados) -- para el resto de los casos (`Result.Failure` de negocio, `ValidationProblem`)
   * NINGÚN correlation id viaja hoy en la respuesta HTTP, ni como header (`traceparent`/
   * `X-Correlation-Id`) ni como extensión. Este método deja preparados ambos caminos (header y cuerpo)
   * para cuando el backend lo incorpore de forma consistente, sin fingir que ya existe.
   */
  private extractCorrelationId(error: HttpErrorResponse, problemDetails: ProblemDetails | undefined): string | undefined {
    const correlationHeader = error.headers?.get(CORRELATION_ID_HEADER);
    if (correlationHeader) {
      return correlationHeader;
    }

    const traceparentHeader = error.headers?.get(TRACEPARENT_HEADER);
    if (traceparentHeader) {
      const traceId = this.extractTraceIdFromTraceparent(traceparentHeader);
      if (traceId) {
        return traceId;
      }
    }

    if (problemDetails?.traceId && typeof problemDetails.traceId === 'string') {
      return problemDetails.traceId;
    }

    const correlationExtension = problemDetails?.['correlationId'];
    if (typeof correlationExtension === 'string' && correlationExtension.length > 0) {
      return correlationExtension;
    }

    return undefined;
  }

  /** `traceparent` (W3C Trace Context): `<version>-<trace-id (32 hex)>-<parent-id (16 hex)>-<flags>`. */
  private extractTraceIdFromTraceparent(traceparent: string): string | undefined {
    const parts = traceparent.split('-');
    return parts.length === 4 && parts[1].length === 32 ? parts[1] : undefined;
  }
}
