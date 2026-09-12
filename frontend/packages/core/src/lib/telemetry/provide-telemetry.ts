import { ENVIRONMENT_INITIALIZER, EnvironmentProviders, Provider, inject, makeEnvironmentProviders } from '@angular/core';
import { BitcodeTelemetryService } from './telemetry.service';
import { observeBitcodeWebVitals } from './web-vitals';

/**
 * Registra la telemetría "pasiva" de F7-13 en `app.config.ts`: errores de JS no controlados
 * (`window.onerror`/`unhandledrejection`) y Core Web Vitals. Separado de `bitcodeTelemetryInterceptor`
 * (que cubre las llamadas HTTP, y se registra aparte vía `provideHttpClient(withInterceptors([...]))`
 * porque un interceptor no puede inscribirse a sí mismo como provider de un solo lado) -- una app real
 * debe registrar AMBOS para tener la cobertura completa del criterio de aceptación ("Web vitals, errores,
 * trazas y contexto").
 *
 * No-op seguro fuera de un browser (SSR, tests sin `window`) -- ver guardas en cada listener.
 */
export function provideBitcodeTelemetry(): EnvironmentProviders {
  const initializer: Provider = {
    provide: ENVIRONMENT_INITIALIZER,
    multi: true,
    useValue: () => {
      const telemetry = inject(BitcodeTelemetryService);
      registerUncaughtErrorListeners(telemetry);
      observeBitcodeWebVitals(telemetry);
    },
  };
  return makeEnvironmentProviders([initializer]);
}

function registerUncaughtErrorListeners(telemetry: BitcodeTelemetryService): void {
  if (typeof window === 'undefined') {
    return;
  }

  window.addEventListener('error', (event: ErrorEvent) => {
    telemetry.recordUncaughtError(event.message, {
      source: event.filename,
      line: event.lineno,
      column: event.colno,
    });
  });

  window.addEventListener('unhandledrejection', (event: PromiseRejectionEvent) => {
    const reason = event.reason as unknown;
    const message = reason instanceof Error ? reason.message : String(reason);
    telemetry.recordUncaughtError(message, { unhandledRejection: true });
  });
}
