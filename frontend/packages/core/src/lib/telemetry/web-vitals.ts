import { BitcodeTelemetryService } from './telemetry.service';

/**
 * Colector mínimo de Core Web Vitals (F7-13: "Web vitals") vía `PerformanceObserver` nativo -- sin agregar
 * la dependencia `web-vitals` de Google: el volumen de métricas necesario hoy (LCP, CLS, FID) es chico y
 * `PerformanceObserver` ya cubre los `entryType` necesarios sin una librería externa. Si el volumen de
 * métricas crece (p. ej. INP con su cálculo más elaborado de percentiles), migrar a `web-vitals` sería una
 * decisión de una tarea futura, no de F7-13.
 *
 * Guardado explícitamente para no explotar en entornos sin `PerformanceObserver` (SSR, algunos entornos de
 * test) -- `observeBitcodeWebVitals` es un no-op seguro ahí.
 */
export function observeBitcodeWebVitals(telemetry: BitcodeTelemetryService): void {
  if (typeof PerformanceObserver === 'undefined') {
    return;
  }

  observeLargestContentfulPaint(telemetry);
  observeCumulativeLayoutShift(telemetry);
  observeFirstInputDelay(telemetry);
}

function observeLargestContentfulPaint(telemetry: BitcodeTelemetryService): void {
  try {
    const observer = new PerformanceObserver((list) => {
      const entries = list.getEntries();
      const last = entries[entries.length - 1];
      if (last) {
        telemetry.recordWebVital('LCP', last.startTime);
      }
    });
    observer.observe({ type: 'largest-contentful-paint', buffered: true });
  } catch {
    // Entrada no soportada por el motor/entorno (p. ej. jsdom) -- degradación silenciosa, no crítica.
  }
}

function observeCumulativeLayoutShift(telemetry: BitcodeTelemetryService): void {
  try {
    let cumulative = 0;
    const observer = new PerformanceObserver((list) => {
      for (const entry of list.getEntries() as Array<PerformanceEntry & { value: number; hadRecentInput?: boolean }>) {
        if (!entry.hadRecentInput) {
          cumulative += entry.value;
        }
      }
      telemetry.recordWebVital('CLS', cumulative);
    });
    observer.observe({ type: 'layout-shift', buffered: true });
  } catch {
    // Idem LCP.
  }
}

function observeFirstInputDelay(telemetry: BitcodeTelemetryService): void {
  try {
    const observer = new PerformanceObserver((list) => {
      for (const entry of list.getEntries() as Array<PerformanceEntry & { processingStart: number }>) {
        telemetry.recordWebVital('FID', entry.processingStart - entry.startTime);
      }
    });
    observer.observe({ type: 'first-input', buffered: true });
  } catch {
    // Idem LCP.
  }
}
