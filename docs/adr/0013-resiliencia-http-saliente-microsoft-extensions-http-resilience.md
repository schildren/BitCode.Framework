# 0013. Resiliencia HTTP saliente: `Microsoft.Extensions.Http.Resilience` (Polly v8) con retry condicionado al método

**Estado:** Accepted
**Fecha:** 2026-09-06
**Responsable:** Pendiente de asignación

## Contexto

F1-26 (Fase 1 — BitCode Core 2.0, Épica F1-E — Confiabilidad y resiliencia) pide incorporar timeout,
retry con jitter, circuit breaker y bulkhead para llamadas HTTP salientes de un proyecto consumidor
hacia un servicio externo, con "Retry solo en operaciones seguras" como criterio de aceptación
literal.

Al momento de ejecutar esta tarea, ningún módulo del framework ni ningún consumidor real (incluido
`samples/Sample.Api`) declara un `HttpClient`/`IHttpClientFactory` saliente propio — el único uso
existente de `HttpClient` en el repo es la instrumentación de OpenTelemetry
(`OpenTelemetry.Instrumentation.Http`, F1-?, que observa llamadas salientes, no las protege). Esta
tarea no tiene todavía un consumidor real que integrar: prepara el mecanismo reutilizable para
cuando un módulo futuro (Fase 2+) necesite llamar a un servicio HTTP externo. No debe confundirse con
F1-10 (timeouts/cancelación de operaciones de **base de datos**, `PersistenceOptions.CommandTimeoutSeconds`),
que es un problema distinto (SQL Server, no HTTP saliente), ni con rate limiting del lado servidor
(proteger `Sample.Api` de sus propios clientes), que queda fuera de alcance de esta tarea.

.NET 10 (F1-01) mantiene `Microsoft.Extensions.Http.Resilience` (paquete de Microsoft construido
sobre Polly v8, `Polly.Core`) como el mecanismo estándar de facto para resiliencia de
`HttpClient` integrado con `IHttpClientFactory`. Expone `AddStandardResilienceHandler()`, que compone
en una sola pipeline (de adentro hacia afuera): timeout por intento (`AttemptTimeout`), retry con
backoff exponencial y jitter, circuit breaker, y un rate limiter de concurrencia (`RateLimiter`) que
cumple el rol de bulkhead — exactamente los cuatro mecanismos que pide el alcance de F1-26, ya
validados por Microsoft, sin necesidad de reimplementar ningún patrón de resiliencia a mano (Polly
`CircuitBreakerStrategy`/`RetryStrategy` manual, semáforos propios para bulkhead, etc.).

## Decisión

**Se adopta `Microsoft.Extensions.Http.Resilience` (`AddStandardResilienceHandler`) como base de la
pipeline de resiliencia HTTP saliente del framework**, envuelta en un método de extensión propio
(`Shared.Infrastructure.Http`, `HttpServiceCollectionExtensions.AddResilientHttpClient<TClient>`) que:

1. Registra `TClient` como cliente HTTP tipado (`AddHttpClient<TClient>()`) con la pipeline estándar.
2. Expone como configurable (`HttpResilienceOptions`, vía `Action<HttpResilienceOptions>` en código o
   `IConfiguration`/`appsettings`) únicamente los valores numéricos de afinación: cantidad de
   reintentos, demora base del backoff, timeouts (por intento y total), umbrales del circuit breaker
   (`FailureRatio`, `MinimumThroughput`, `SamplingDuration`, `BreakDuration`) y límites de
   concurrencia (`MaxConcurrentRequests`, `ConcurrencyQueueLimit`).
3. **Fija siempre, sin exponerlo como configurable, el predicado de "qué es seguro reintentar"**
   (`HttpRetrySafety.CreateShouldHandlePredicate`) — esta es la pieza central de esta decisión y del
   criterio de aceptación literal de F1-26.

### "Retry solo en operaciones seguras": predicado condicionado al método HTTP, no al código de estado

Por defecto, `AddStandardResilienceHandler()` de Microsoft reintenta ante cualquier falla transitoria
(5xx, 408, `HttpRequestException`, timeout de intento) **sin mirar el método HTTP del request** — un
`POST` que falla con 503 se reintentaría exactamente igual que un `GET`. Eso es insuficiente para el
criterio de aceptación de F1-26: un `POST` típico ("crear un recurso", "cobrar un pago") repetido sin
que el receptor lo deduplique puede duplicar el efecto de negocio, aunque la falla original haya sido
transitoria del lado de la red.

`HttpRetrySafety.IsSafeToRetry(HttpRequestMessage)` (`Shared.Infrastructure.Http.Resilience`)
resuelve esto clasificando el request, antes de siquiera evaluar si la falla fue transitoria:

- **Seguro por especificación HTTP (RFC 9110):** `GET`, `HEAD` (solo lectura) y `PUT`, `DELETE`
  (idempotentes por definición del protocolo — aplicar la misma operación N veces produce el mismo
  resultado que aplicarla una vez).
- **Nunca seguro por defecto:** `POST`, `PATCH` — no hay garantía de idempotencia del lado del
  receptor solo por el método.
- **Excepción explícita:** un `POST`/`PATCH` que el llamador marca con
  `HttpRequestMessage.MarkSafeToRetry()` (una opción de request, no una constante global — decisión
  por request, no por cliente), o que ya trae el header `Idempotency-Key` seteado (mismo nombre de
  header que usa `IIdempotentCommand`/`HttpContextIdempotencyKeyProvider` del lado entrante del
  framework, F1-22) — en ambos casos, el llamador declara explícitamente que el servicio externo
  puede deduplicar la operación por su cuenta.

El predicado combina esta clasificación con `HttpClientResiliencePredicates.IsTransient` (la
detección estándar de fallas transitorias de Microsoft): un request inseguro de reintentar nunca
llega siquiera a evaluar si la falla fue transitoria — se descarta primero por su método, no por el
código de estado de la respuesta.

`args.Context.GetRequestMessage()` (extensión pública de `Polly.HttpResilienceContextExtensions`) es
el mecanismo que permite recuperar el `HttpRequestMessage` original dentro del predicado
`RetryStrategyOptions<HttpResponseMessage>.ShouldHandle` — Microsoft ya lo asocia al
`ResilienceContext` de cada llamada antes de ejecutar la pipeline; no requiere ningún estado adicional
propio del framework.

### Verificación con tests reales, sin Docker ni red real

`tests/Shared.Infrastructure.Http.Tests/Resilience/HttpServiceCollectionExtensionsResilienceTests.cs`
registra un `HttpClient` tipado real vía `AddResilientHttpClient<TClient>` con un
`FakeHttpMessageHandler` (doble de prueba controlable) en el fondo de la pipeline, sin ningún
`HttpMessageHandler` de red real:

- Un `GET` que falla dos veces con 503 y recién responde 200 al tercer intento: la pipeline reintenta
  automáticamente y el `HttpClient` recibe la respuesta 200 final (`Get_FallaTransitoria503_...`).
- Un `POST` sin `Idempotency-Key` ni marca explícita que siempre falla con 503: el
  `FakeHttpMessageHandler` se invoca exactamente una vez — la pipeline nunca reintenta, y el llamador
  recibe el 503 original (`Post_SinMarcadorDeIdempotencia_FallaSinReintentarNiDuplicar`).
- El mismo `POST`, con el header `Idempotency-Key` presente, sí se reintenta y termina en éxito
  (`Post_ConHeaderIdempotencyKey_SeReintentaComoUnaOperacionSegura`) — confirma que la excepción es
  intencional y funciona, no que el mecanismo simplemente ignora `POST`.
- Dos `POST` consecutivos que fallan (con umbrales de circuit breaker bajos a propósito para el test)
  abren el circuito; una tercera llamada falla de inmediato con `Polly.CircuitBreaker.BrokenCircuitException`
  **sin volver a invocar** el `FakeHttpMessageHandler` — verifica "falla rápido" en vez de colgar el
  timeout completo en cada intento (`CircuitBreaker_AbreTrasFallosConsecutivos_...`).

`tests/Shared.Infrastructure.Http.Tests/Resilience/HttpRetrySafetyTests.cs` cubre unitariamente la
clasificación de `IsSafeToRetry` para cada método HTTP, el marcador explícito, el header
`Idempotency-Key` y el caso sin `HttpRequestMessage` disponible (falla cerrado: nunca seguro).

## Alternativas consideradas

- **Reimplementar retry/circuit breaker/bulkhead a mano** (semáforos propios, `Polly.Core` de bajo
  nivel sin la integración de `Microsoft.Extensions.Http.Resilience` con `IHttpClientFactory`):
  descartada — el Plan Maestro (sección 13, guía de la tarea) pide explícitamente preferir la
  librería estándar de .NET sobre reinventar un patrón ya resuelto y validado por Microsoft. El único
  valor que agrega el framework es la política de seguridad de retry por método HTTP, que no viene
  resuelta por defecto en el paquete de Microsoft.
- **Exponer el predicado de retry seguro como configurable** (permitir que un proyecto consumidor
  reintente `POST` sin marcador): descartada — convertiría el criterio de aceptación literal de F1-26
  en una recomendación en vez de una garantía; cualquier proyecto que necesite reintentar un `POST`
  real debe declararlo explícitamente por request (`MarkSafeToRetry`/header `Idempotency-Key`), nunca
  deshabilitando la protección para todos los requests de un cliente.
- **Marcar como seguro cualquier método con `Idempotency-Key` sin importar el servicio externo**:
  aceptada parcialmente — el framework confía en la declaración del llamador (mismo principio de
  confianza que el resto de sus controles: quien marca `IIdempotentCommand`/`MarkSafeToRetry` es
  responsable de que sea cierto), no intenta verificar contra el servicio externo que efectivamente
  soporta idempotencia — verificarlo no es técnicamente posible desde el lado del cliente.

## Consecuencias

- Nuevo proyecto público `Shared.Infrastructure.Http` (namespace
  `BitCode.Framework.Shared.Infrastructure.Http`), sin consumidor real todavía: cero cambio de
  comportamiento para cualquier proyecto existente hasta que un módulo futuro llame
  `AddResilientHttpClient<TClient>`.
- Un proyecto consumidor que registre un `HttpClient` saliente por su cuenta (sin pasar por
  `AddResilientHttpClient`) no obtiene ninguna de estas protecciones — el método de extensión no
  intercepta clientes registrados de otra forma. Documentado en `docs/convenciones.md` como la única
  vía recomendada para un `HttpClient` saliente nuevo.
- Un `POST`/`PATCH` hacia un servicio externo que sí es idempotente por diseño (por ejemplo, el
  servicio ya deduplica por una clave de negocio propia) requiere que el llamador lo declare
  explícitamente (`MarkSafeToRetry()` o header `Idempotency-Key`) — omitir esa declaración no es un
  bug, es el comportamiento seguro por defecto.
- Los valores por defecto de `HttpResilienceOptions` (3 reintentos, 10 s por intento, 30 s total,
  circuit breaker a 10 % de fallos sobre mínimo 10 llamadas en 30 s, bulkhead de 100 llamadas
  concurrentes sin cola) son un punto de partida razonable para un servicio genérico, no una
  recomendación validada contra ningún SLA real — cada integración futura debe revisarlos contra el
  comportamiento documentado del servicio que consume.

## Riesgos y mitigación

- **Riesgo:** un futuro colaborador registra un `HttpClient` con `AddHttpClient` directo (sin pasar
  por `AddResilientHttpClient`) por desconocer el mecanismo, perdiendo todas las protecciones.
  **Mitigación:** `docs/convenciones.md` documenta `AddResilientHttpClient<TClient>` como la única vía
  recomendada para un `HttpClient` saliente nuevo del framework.
- **Riesgo:** un consumidor marca un `POST` con `MarkSafeToRetry()` sin que el servicio externo
  realmente sea idempotente, duplicando un efecto de negocio bajo fallas transitorias repetidas.
  **Mitigación:** la documentación (XML docs de `MarkSafeToRetry` y esta ADR) es explícita sobre que
  la responsabilidad de esa garantía es del llamador, no del framework — no hay forma de verificarlo
  automáticamente desde el cliente.
- **Riesgo:** los valores por defecto de circuit breaker/bulkhead resultan inadecuados para un
  servicio externo real de alto volumen (por ejemplo, un `MinimumThroughput` de 10 demasiado bajo para
  una integración con miles de llamadas por segundo). **Mitigación:** todos los valores numéricos son
  configurables por cliente vía `HttpResilienceOptions`; no hay ningún valor hardcodeado sin punto de
  extensión.
- Vinculado al registro de riesgos de F0-12 (pendiente de creación).
