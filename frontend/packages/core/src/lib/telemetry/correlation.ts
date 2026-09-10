/**
 * Genera un `traceparent` (W3C Trace Context) nuevo para correlacionar una llamada HTTP saliente con
 * logs/trazas -- MISMO formato que `BitcodeErrorExperienceService.extractTraceIdFromTraceparent` (F7-06)
 * ya sabe parsear si viene de vuelta en un header de respuesta.
 *
 * **Limitación honesta (mismo criterio que F7-06, no un supuesto de diseño):** verificado en el código real
 * del backend (`Shared.Infrastructure.Web`, `Shared.Infrastructure.Observability`) que ningún servicio del
 * repositorio LEE hoy un header `traceparent` entrante para continuar la traza -- `AddAspNetCoreInstrumentation`
 * genera su propio trace id interno para el span del request. Enviar este header desde el frontend es
 * preparar el lado cliente de la correlación (y no rompe nada -- un backend que lo ignore simplemente no lo
 * usa), no una promesa de que la correlación end-to-end ya funciona hoy.
 */
export function generateTraceparent(): string {
  const traceId = randomHex(32);
  const spanId = randomHex(16);
  return `00-${traceId}-${spanId}-01`;
}

function randomHex(length: number): string {
  let result = '';
  for (let i = 0; i < length; i += 1) {
    result += Math.floor(Math.random() * 16).toString(16);
  }
  return result;
}
