# Guía — Resiliencia HTTP saliente (F1-26)

## Qué problema resuelve

Un `HttpClient` sin ninguna política de resiliencia, hablándole a un servicio externo, tiene tres
fallas típicas en producción:

1. **Cuelga el request del llamador** si el servicio externo no responde (sin timeout, un `await
   client.GetAsync(...)` puede tardar indefinidamente).
2. **No se recupera solo de una falla transitoria** (una caída momentánea de red, un 503 puntual) sin
   que el llamador implemente su propio retry a mano, típicamente sin jitter — lo que provoca que,
   si varias instancias del mismo servicio fallan al mismo tiempo, todas reintenten simultáneamente
   contra la dependencia que recién se está recuperando ("thundering herd"), volviendo a tumbarla.
3. **Sigue intentando contra un servicio caído** en vez de fallar rápido: cada llamada nueva paga el
   timeout completo antes de fallar, en vez de detectar que el servicio está caído y devolver el
   error de inmediato durante un tiempo (circuit breaker), ni limita cuántas llamadas concurrentes
   puede tener en vuelo hacia ese servicio (bulkhead), arriesgando saturar los propios recursos del
   proceso (hilos, sockets) con llamadas paralelas ilimitadas.

`Shared.Infrastructure.Http` (`AddResilientHttpClient<TClient>`) resuelve los tres con
`Microsoft.Extensions.Http.Resilience` (Polly v8), el mecanismo estándar de .NET para
`IHttpClientFactory` — no es una implementación propia del framework.

**Estado actual:** ningún módulo del framework ni `samples/Sample.Api` declara todavía un
`HttpClient` saliente real. Esta guía documenta el mecanismo reutilizable a usar cuando un módulo
futuro (Fase 2+) necesite llamar a un servicio HTTP externo — ver ADR
[0013](adr/0013-resiliencia-http-saliente-microsoft-extensions-http-resilience.md).

## Cómo registrar un cliente HTTP resiliente

```csharp
// InfrastructureModule.cs (o donde el módulo registre sus servicios)
public class MiServicioExternoClient(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public Task<HttpResponseMessage> ObtenerRecursoAsync(string id, CancellationToken ct) =>
        _httpClient.GetAsync($"recursos/{id}", ct);
}

services.AddResilientHttpClient<MiServicioExternoClient>(options =>
    {
        options.RetryMaxAttempts = 3;
        options.AttemptTimeout = TimeSpan.FromSeconds(5);
    })
    .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://servicio-externo.ejemplo.com/"));
```

`AddResilientHttpClient<TClient>` devuelve `IHttpClientBuilder` (el mismo tipo que
`IServiceCollection.AddHttpClient<TClient>()`), así que se puede seguir encadenando cualquier
configuración adicional estándar de `IHttpClientFactory` (`ConfigureHttpClient`, `AddHttpMessageHandler`,
etc.).

Sobrecargas disponibles:

| Sobrecarga | Cuándo usarla |
|---|---|
| `AddResilientHttpClient<TClient>()` | Valores por defecto de `HttpResilienceOptions` alcanzan. |
| `AddResilientHttpClient<TClient>(Action<HttpResilienceOptions>)` | Ajustar timeouts/reintentos/umbrales en código, para una integración puntual. |
| `AddResilientHttpClient<TClient>(IConfiguration, sectionName?)` | Ajustar desde `appsettings` (sección `"HttpResilience:{NombreDeTClient}"` por defecto). |

## Qué incluye la pipeline (y qué es configurable)

| Mecanismo | Configurable vía `HttpResilienceOptions` | Default |
|---|---|---|
| Timeout por intento | `AttemptTimeout` | 10 s |
| Timeout total (incluye reintentos) | `TotalTimeout` | 30 s |
| Retry (cantidad, demora base, backoff exponencial + jitter obligatorio) | `RetryMaxAttempts`, `RetryBaseDelay` | 3 intentos, 1 s base |
| Circuit breaker (proporción de fallos, mínimo de llamadas, ventana, duración de apertura) | `CircuitBreakerFailureRatio`, `CircuitBreakerMinimumThroughput`, `CircuitBreakerSamplingDuration`, `CircuitBreakerBreakDuration` | 10 % / 10 llamadas / 30 s / 5 s |
| Bulkhead (concurrencia máxima, cola) | `MaxConcurrentRequests`, `ConcurrencyQueueLimit` | 100 / 0 (sin cola) |
| **Qué operaciones se reintentan** | **No configurable** — ver siguiente sección | GET/HEAD/PUT/DELETE, o cualquier método marcado explícitamente |

El jitter del retry **siempre** está activo — no es un valor que se pueda desactivar desde
`HttpResilienceOptions`: es la mitigación estándar contra "thundering herd" cuando varias instancias
del mismo servicio consumidor reintentan al mismo tiempo contra una dependencia que recién se está
recuperando.

## Retry solo en operaciones seguras (criterio de aceptación de F1-26)

**Un `POST`/`PATCH` nunca se reintenta automáticamente por defecto**, sin importar si el servicio
externo respondió un código transitorio (503, 500, timeout). Reintentar un `POST` típico ("crear un
recurso", "cobrar un pago") sin que el receptor lo deduplique puede duplicar el efecto de negocio —
el framework prefiere devolver el error original antes que arriesgar esa duplicación.

`HttpRetrySafety.IsSafeToRetry(HttpRequestMessage)` (`Shared.Infrastructure.Http.Resilience`)
clasifica cada request saliente:

- **Siempre seguro** (idempotente por especificación HTTP, RFC 9110): `GET`, `HEAD`, `PUT`, `DELETE`.
- **Nunca seguro por defecto:** `POST`, `PATCH`.
- **Excepción explícita** — un `POST`/`PATCH` puntual que el llamador **sabe** que el servicio externo
  puede deduplicar:

```csharp
// Opción 1: el servicio ya requiere/soporta un header de idempotencia propio
using var request = new HttpRequestMessage(HttpMethod.Post, "pagos");
request.Headers.Add("Idempotency-Key", claveDeIdempotencia);
await httpClient.SendAsync(request, ct);

// Opción 2: marca explícita sin depender de un header concreto
using var request = new HttpRequestMessage(HttpMethod.Post, "pagos");
request.MarkSafeToRetry();
await httpClient.SendAsync(request, ct);
```

Esta condición **no es configurable** desde `HttpResilienceOptions` — es una garantía dura del
framework, no un parámetro de afinación (ver ADR 0013, sección "Alternativas consideradas").

## Qué NO hacer

- **No** llamar `services.AddHttpClient<TClient>()` directo para un cliente saliente nuevo del
  framework, esperando que herede protecciones que no tiene — `AddResilientHttpClient<TClient>` es la
  única vía que registra la pipeline de resiliencia.
- **No** usar `MarkSafeToRetry()`/el header `Idempotency-Key` en un `POST` "para que ande sin
  reintentar a mano" sin haber confirmado que el servicio externo realmente deduplica esa operación —
  el framework confía en la declaración del llamador, no puede verificarla.
- **No** confundir esta pipeline con timeouts/cancelación de operaciones de base de datos
  (`PersistenceOptions.CommandTimeoutSeconds`, F1-10) ni con rate limiting del lado servidor (proteger
  este proceso de sus propios clientes) — ambos son mecanismos distintos, no relacionados.

## Pruebas de referencia

- `tests/Shared.Infrastructure.Http.Tests/Resilience/HttpRetrySafetyTests.cs` — clasificación de
  `IsSafeToRetry` por método HTTP, marca explícita, header `Idempotency-Key` y ausencia de request.
- `tests/Shared.Infrastructure.Http.Tests/Resilience/HttpServiceCollectionExtensionsResilienceTests.cs`
  — pipeline real registrada por `AddResilientHttpClient<TClient>` contra un
  `FakeHttpMessageHandler` (sin Docker ni red real): un `GET` que falla transitoriamente se reintenta
  y termina en éxito; un `POST` sin marcador de idempotencia falla sin reintentar; el mismo `POST` con
  `Idempotency-Key` sí se reintenta; y el circuit breaker se abre tras fallos consecutivos, fallando
  rápido con `Polly.CircuitBreaker.BrokenCircuitException` sin volver a invocar el handler.
