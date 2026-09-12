# Guía — Telemetría frontend (F7-13, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-13 (Telemetría) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Criterio de
> aceptación literal: "Web vitals, errores, trazas y contexto... Correlación con backend". Entregable:
> `BitcodeTelemetryService` + interceptor de trazas HTTP + colector de Core Web Vitals, todo en
> `@bitcode/core` (mismo criterio que F7-06/F7-11/F7-12: no se creó un paquete nuevo). **No** incluye un
> backend real de telemetría ni integración con un proveedor de observabilidad concreto (ver sección 5).

## 1. Dónde vive cada pieza

| Pieza | Ubicación |
|---|---|
| `BitcodeTelemetryEvent`, `BitcodeTelemetrySink`, `ConsoleTelemetrySink`, `BITCODE_TELEMETRY_SINK`, `BITCODE_TELEMETRY_CONTEXT` | `frontend/packages/core/src/lib/telemetry/telemetry.model.ts` |
| `generateTraceparent` | `frontend/packages/core/src/lib/telemetry/correlation.ts` |
| `BitcodeTelemetryService` | `frontend/packages/core/src/lib/telemetry/telemetry.service.ts` |
| `bitcodeTelemetryInterceptor` | `frontend/packages/core/src/lib/telemetry/telemetry.interceptor.ts` |
| `observeBitcodeWebVitals` | `frontend/packages/core/src/lib/telemetry/web-vitals.ts` |
| `provideBitcodeTelemetry` | `frontend/packages/core/src/lib/telemetry/provide-telemetry.ts` |

## 2. Las cuatro piezas del criterio de aceptación

- **Errores**: `BitcodeTelemetryService.recordError(uiError, extra?)` reutiliza el `BitcodeUiError` ya
  clasificado por `BitcodeErrorExperienceService` (F7-06) -- telemetría nunca reinterpreta un error HTTP por
  su cuenta. `recordUncaughtError(message, extra?)` cubre lo que NO pasa por HTTP: excepciones de JS no
  controladas, capturadas vía `window.addEventListener('error'/'unhandledrejection')` en
  `provideBitcodeTelemetry()`.
- **Web vitals**: `observeBitcodeWebVitals` captura LCP (`largest-contentful-paint`), CLS
  (`layout-shift`, acumulado excluyendo `hadRecentInput`) y FID (`first-input`) vía `PerformanceObserver`
  nativo -- sin agregar la librería `web-vitals` (decisión de diseño, ver sección 5).
- **Trazas**: `bitcodeTelemetryInterceptor` agrega un header `traceparent` (W3C Trace Context, mismo formato
  que `BitcodeErrorExperienceService.extractTraceIdFromTraceparent` ya sabe leer) a cada request HTTP
  saliente que no lo tenga, mide la duración real con `performance.now()` y emite un evento `'trace'` al
  completarse (éxito o error).
- **Contexto**: `BITCODE_TELEMETRY_CONTEXT` (`provideBitcodeTelemetryContext(context)`) -- un objeto estático
  que se mergea en TODOS los eventos (p. ej. nombre/versión de la app, ambiente). Deliberadamente no incluye
  datos de sesión/usuario por defecto (evita filtrar PII a un sink sin decisión explícita de la app
  consumidora).

## 3. Correlación con backend: qué existe hoy, honestamente

Mismo criterio de honestidad que `docs/guia-frontend-errores.md` (sección 2, F7-06): se verificó el código
real del backend antes de documentar esto, no se asumió.

`bitcodeTelemetryInterceptor` envía un `traceparent` W3C válido en cada request. **Ningún servicio del
repositorio lee hoy ese header para continuar la traza** -- `AddAspNetCoreInstrumentation`
(`Shared.Infrastructure.Observability`) genera su propio trace id interno por request, independiente del
`traceparent` entrante. Enviar el header desde el frontend deja preparado el lado cliente de la correlación
(un backend que lo ignore simplemente no lo usa, no rompe nada) -- **no** es una promesa de que la
correlación end-to-end funcione hoy. Habilitarla del lado del backend (leer `traceparent` entrante y
continuar el `Activity` en vez de crear uno nuevo) es trabajo de una tarea de observabilidad de backend
futura, fuera de alcance de F7-13.

Lo que sí funciona hoy, verificado con test real: el mismo `correlationId` (`traceparent`) queda en el
evento `'trace'` (éxito o error) Y, si el request falló, en el evento `'error'` derivado (vía
`BitcodeUiError.correlationId`, cuando el backend lo expone -- ver limitación de F7-06) -- permite
correlacionar ambos eventos del lado del cliente/sink, aunque el backend todavía no cierre el círculo.

## 4. Registro en `app.config.ts`

Dos piezas separadas porque un interceptor no puede auto-registrarse como listener global:

```ts
import { provideBitcodeTelemetry, bitcodeTelemetryInterceptor, bitcodeErrorInterceptor } from '@bitcode/core';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBitcodeTelemetry(), // errores no controlados + web vitals
    provideHttpClient(withInterceptors([bitcodeTelemetryInterceptor, bitcodeErrorInterceptor])),
  ],
};
```

Orden recomendado (documentado también en el propio `telemetry.interceptor.ts`):
`[bitcodeTelemetryInterceptor, bitcodeErrorInterceptor]` -- el `traceparent` ya está en el request cuando
`bitcodeErrorInterceptor` lo procesa, y `bitcodeTelemetryInterceptor` ve el error DESPUÉS de que
`bitcodeErrorInterceptor` ya lo clasificó, evitando reclasificar el mismo `HttpErrorResponse` dos veces.

`apps/shell/src/app/app.config.ts` no se modificó en esta tarea (mismo criterio que F7-06: no hay todavía
una llamada HTTP de negocio real en el shell que se beneficie) -- un consumidor real de un módulo de negocio
debe registrar ambas piezas siguiendo el orden de arriba.

## 5. Decisiones de diseño

- **Sink por defecto = consola** (`ConsoleTelemetrySink`): ninguna aplicación de referencia real consume
  estos eventos en un backend de telemetría todavía (esa aplicación de referencia es Fase 8, "una nueva
  aplicación desarrolla principalmente su lógica de negocio y reutiliza capacidades empresariales
  maduras"). `BitcodeTelemetrySink` es la interfaz de extensión -- una app real la implementa contra
  `navigator.sendBeacon`/el SDK de un proveedor de observabilidad concreto vía `provideBitcodeTelemetrySink`.
- **`PerformanceObserver` nativo en vez de la librería `web-vitals`** de Google: el volumen de métricas
  necesario hoy (LCP/CLS/FID) ya está cubierto por `PerformanceObserver` sin agregar una dependencia
  externa. Migrar a `web-vitals` (que calcula INP con percentiles más precisos, entre otras mejoras) sería
  candidato de una tarea futura si el volumen de métricas lo justifica -- documentado en el código, no una
  decisión silenciosa.
- **Sin proveedor de observabilidad elegido** (Application Insights, Datadog RUM, Sentry, etc.): fuera de
  las "Decisiones que requieren aprobación humana" del plan no aparece explícitamente elección de proveedor
  de telemetría frontend, pero por prudencia se dejó el sink desacoplado (`BitcodeTelemetrySink`) en vez de
  atarse a un SDK concreto -- si en el futuro se decide un proveedor, es una integración de una tarea nueva,
  no un cambio de contrato de `@bitcode/core`.

## 6. Cómo se probó

`frontend/packages/core/src/lib/telemetry/*.spec.ts`: `correlation.spec.ts` (formato W3C válido),
`telemetry.service.spec.ts` (los 4 tipos de evento + merge de contexto estático, con un sink de prueba que
graba eventos), `telemetry.interceptor.spec.ts` (contra `ProblemDetailsTestServer`, servidor HTTP real --
mismo patrón que `error.interceptor.spec.ts`: verifica que agrega `traceparent`, que NO lo sobreescribe si
ya viene en el request, y que en un error HTTP registra trace+error y relanza el error original sin
transformarlo), `web-vitals.spec.ts`/`provide-telemetry.spec.ts` (no lanzan en jsdom, y el listener de
`window.error` funciona end-to-end con un `ErrorEvent` real).

Comandos ejecutados:

```bash
cd frontend
npx nx run core:test
npx nx run core:lint
npx nx run-many -t build test lint
```

Resultado: 56 tests en `core` (15 nuevos de telemetría + 41 preexistentes), `core:lint` limpio, y
`nx run-many -t build test lint` sigue pasando para los 8 proyectos del workspace (25 tareas), sin
regresiones.

## 7. Limitaciones y pendientes explícitos (fuera de alcance de F7-13)

- **Sin backend/proveedor de telemetría real conectado** -- el sink por defecto es la consola del browser
  (ver sección 5); ningún dato de telemetría se envía hoy a ningún destino persistente.
- **Correlación con backend no cerrada del lado del servidor** (ver sección 3) -- el frontend envía
  `traceparent`, pero ningún backend del repo lo lee todavía para continuar la traza.
- **`apps/shell` no registró `provideBitcodeTelemetry`/`bitcodeTelemetryInterceptor`** -- mismo criterio que
  F7-06 (sección 4 de esa guía): sin una llamada HTTP de negocio real en el shell que lo justifique hoy.
- **Sin dashboard/visualización** -- esta tarea entrega la instrumentación del lado del cliente, no un panel
  de consumo (eso depende del proveedor de observabilidad que se elija a futuro, fuera de alcance).
- **INP (Interaction to Next Paint) no capturado** -- sólo LCP/CLS/FID; INP reemplazó a FID como Core Web
  Vital oficial de Google en 2024, pero su cálculo correcto (percentil 98 de todas las interacciones de la
  sesión) es más elaborado que lo que un `PerformanceObserver` simple puede hacer razonablemente -- candidato
  para cuando se evalúe migrar a la librería `web-vitals` (ver sección 5).
