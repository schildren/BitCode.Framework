import { Injectable, inject } from '@angular/core';
import { BitcodeUiError } from '../errors/bitcode-error.model';
import { BITCODE_TELEMETRY_CONTEXT, BITCODE_TELEMETRY_SINK, BitcodeTelemetryEvent } from './telemetry.model';

/**
 * Punto único de emisión de telemetría de F7-13. Ningún consumidor debería llamar al sink directamente --
 * mismo criterio de "un único lugar decide" que `BitcodeErrorExperienceService` (F7-06): acá se decide qué
 * `context` agrega cada evento y qué forma tiene, no en cada componente.
 */
@Injectable({ providedIn: 'root' })
export class BitcodeTelemetryService {
  private readonly sink = inject(BITCODE_TELEMETRY_SINK);
  private readonly staticContext = inject(BITCODE_TELEMETRY_CONTEXT);

  /** Reutiliza la clasificación ya calculada por `BitcodeErrorExperienceService.fromHttpError` (F7-06) en
   * vez de reinterpretar el error -- evita que telemetría y "error experience" diverjan sobre qué es un
   * error de validación vs. uno de servidor. */
  recordError(error: BitcodeUiError, extraContext: Readonly<Record<string, unknown>> = {}): void {
    this.emit({
      kind: 'error',
      name: error.kind,
      correlationId: error.correlationId,
      context: {
        httpStatus: error.httpStatus,
        technicalDetail: error.technicalDetail,
        ...extraContext,
      },
    });
  }

  /** Para excepciones de JS no controladas (`window.onerror`/`unhandledrejection`), sin pasar por HTTP --
   * `BitcodeUiError` no aplica porque no hay una respuesta HTTP que clasificar. */
  recordUncaughtError(message: string, extraContext: Readonly<Record<string, unknown>> = {}): void {
    this.emit({ kind: 'error', name: 'uncaught', context: { message, ...extraContext } });
  }

  recordWebVital(name: string, value: number, extraContext: Readonly<Record<string, unknown>> = {}): void {
    this.emit({ kind: 'web-vital', name, value, context: extraContext });
  }

  /** Una llamada HTTP saliente -- `correlationId` es el `traceparent` generado por
   * `bitcodeTelemetryInterceptor` (`correlation.ts`), lo que permite correlacionar ESTE evento con logs del
   * backend el día que un backend real lo consuma (ver limitación honesta en `correlation.ts`). */
  recordTrace(
    name: string,
    correlationId: string,
    durationMs: number,
    extraContext: Readonly<Record<string, unknown>> = {},
  ): void {
    this.emit({ kind: 'trace', name, value: durationMs, correlationId, context: extraContext });
  }

  private emit(event: Omit<BitcodeTelemetryEvent, 'timestamp' | 'context'> & { context: Readonly<Record<string, unknown>> }): void {
    this.sink.send({
      ...event,
      timestamp: Date.now(),
      context: { ...this.staticContext, ...event.context },
    });
  }
}
