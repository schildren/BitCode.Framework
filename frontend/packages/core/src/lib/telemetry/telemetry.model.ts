import { InjectionToken, Provider } from '@angular/core';

/**
 * Tipos de evento de telemetría de F7-13 ("Web vitals, errores, trazas y contexto"):
 *
 * - `'error'`: una excepción de JS no controlada (`window.onerror`/`unhandledrejection`) o un `BitcodeUiError`
 *   mapeado por `BitcodeErrorExperienceService` (F7-06) -- reutiliza esa clasificación en vez de duplicarla.
 * - `'web-vital'`: una métrica Core Web Vitals (`LCP`, `CLS`, `FID`) capturada vía `PerformanceObserver`.
 * - `'trace'`: una llamada HTTP saliente, con su `correlationId` (formato W3C `traceparent`) y duración --
 *   la pieza que permite correlacionar con logs/trazas del backend (ver limitación honesta en
 *   `correlation.ts`: hoy ningún backend del repo consume `traceparent` de vuelta).
 */
export type BitcodeTelemetryEventKind = 'error' | 'web-vital' | 'trace';

export interface BitcodeTelemetryEvent {
  readonly kind: BitcodeTelemetryEventKind;
  readonly name: string;
  readonly timestamp: number;
  readonly value?: number;
  readonly correlationId?: string;
  readonly context: Readonly<Record<string, unknown>>;
}

/** Punto de extensión de a dónde se envía cada evento -- por defecto a la consola (`ConsoleTelemetrySink`),
 * placeholder explícito: ninguna aplicación de referencia real todavía consume estos eventos en un backend
 * de telemetría (fuera de alcance de F7-13, ver guía). Reemplazable con `provideBitcodeTelemetrySink` por
 * un sink real (p. ej. `navigator.sendBeacon` a un endpoint, o el SDK de un proveedor de observabilidad). */
export interface BitcodeTelemetrySink {
  send(event: BitcodeTelemetryEvent): void;
}

export class ConsoleTelemetrySink implements BitcodeTelemetrySink {
  send(event: BitcodeTelemetryEvent): void {
    // Sink por defecto, deliberadamente visible en devtools -- no hay regla no-console activa en este
    // proyecto (ver eslint.config.mjs), así que no se necesita un disable explícito acá.
    console.debug('[bitcode:telemetry]', event);
  }
}

export const BITCODE_TELEMETRY_SINK = new InjectionToken<BitcodeTelemetrySink>('BITCODE_TELEMETRY_SINK', {
  factory: () => new ConsoleTelemetrySink(),
});

export function provideBitcodeTelemetrySink(sink: BitcodeTelemetrySink): Provider[] {
  return [{ provide: BITCODE_TELEMETRY_SINK, useValue: sink }];
}

/** Contexto estático agregado a TODOS los eventos (p. ej. nombre/versión de la app, environment) --
 * "contexto" del criterio de aceptación literal. Deliberadamente NO incluye datos de sesión/usuario por
 * defecto (evita filtrar PII a un sink de telemetría sin que la app consumidora lo decida explícitamente);
 * una app puede agregar `userId`/`tenantId` etc. vía `provideBitcodeTelemetryContext`. */
export const BITCODE_TELEMETRY_CONTEXT = new InjectionToken<Readonly<Record<string, unknown>>>(
  'BITCODE_TELEMETRY_CONTEXT',
  { factory: () => ({}) },
);

export function provideBitcodeTelemetryContext(context: Readonly<Record<string, unknown>>): Provider[] {
  return [{ provide: BITCODE_TELEMETRY_CONTEXT, useValue: context }];
}
