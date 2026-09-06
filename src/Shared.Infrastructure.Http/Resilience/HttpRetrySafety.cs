using System.Net.Http;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace BitCode.Framework.Shared.Infrastructure.Http.Resilience;

/// <summary>
/// Determina si un <see cref="HttpRequestMessage"/> saliente es seguro de reintentar
/// automáticamente (F1-26, criterio de aceptación literal: "Retry solo en operaciones seguras").
/// </summary>
/// <remarks>
/// Un método HTTP es "seguro de reintentar" cuando repetirlo no puede duplicar un efecto de negocio
/// del lado del receptor: <c>GET</c>/<c>HEAD</c> (solo lectura, RFC 9110) y <c>PUT</c>/<c>DELETE</c>
/// (idempotentes por definición del protocolo: aplicar la misma operación N veces produce el mismo
/// resultado que aplicarla una vez, sin importar cuántas veces se repita). <c>POST</c>/<c>PATCH</c>
/// NUNCA se consideran seguros por defecto — un <c>POST</c> típico ("crear un recurso", "cobrar un
/// pago") repetido sin que el receptor lo deduplique explícitamente puede crear el recurso/efecto dos
/// veces. La única excepción es un <c>POST</c>/<c>PATCH</c> que el propio llamador marca
/// explícitamente como seguro con <see cref="MarkSafeToRetry"/> (por ejemplo, porque ya incluye un
/// mecanismo de idempotencia del lado del receptor, como el header <c>Idempotency-Key</c> — mismo
/// concepto que <c>IIdempotentCommand</c> del lado entrante del framework, F1-22) o que ya trae ese
/// header seteado.
/// </remarks>
public static class HttpRetrySafety
{
    /// <summary>
    /// Nombre del header que, si está presente en el request saliente, se interpreta como evidencia
    /// de que el receptor puede deduplicar la operación por su cuenta (mismo nombre de header que usa
    /// <c>IIdempotentCommand</c>/<c>HttpContextIdempotencyKeyProvider</c> del lado entrante, F1-22) —
    /// no implica que este framework implemente ningún mecanismo de idempotencia del lado del
    /// receptor: solo confía en que, si el llamador ya lo declaró, es porque el servicio externo lo
    /// soporta.
    /// </summary>
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";

    /// <summary>
    /// Marca explícita, independiente del método HTTP, de que un request puntual es seguro de
    /// reintentar aunque su método no lo sea por defecto (por ejemplo, un <c>POST</c> hacia un
    /// endpoint que el equipo confirmó que es idempotente por diseño del lado del receptor).
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> SafeToRetryOptionKey = new("BitCode.Http.SafeToRetry");

    /// <summary>
    /// Marca <paramref name="request"/> como seguro de reintentar automáticamente, sin importar su
    /// método HTTP. Usar únicamente cuando el servicio externo garantiza que repetir la llamada no
    /// duplica ningún efecto (por ejemplo, ya valida una clave de idempotencia propia).
    /// </summary>
    public static HttpRequestMessage MarkSafeToRetry(this HttpRequestMessage request)
    {
        request.Options.Set(SafeToRetryOptionKey, true);
        return request;
    }

    /// <summary>
    /// Evalúa si <paramref name="request"/> es seguro de reintentar: método idempotente por RFC 9110
    /// (<c>GET</c>/<c>HEAD</c>/<c>PUT</c>/<c>DELETE</c>), o marcado explícitamente con
    /// <see cref="MarkSafeToRetry"/>, o con el header <see cref="IdempotencyKeyHeaderName"/> ya
    /// presente. <c>null</c> (sin request disponible en el contexto de resiliencia) nunca se
    /// considera seguro — fallar cerrado ante la duda, mismo principio que el resto de los controles
    /// de seguridad del framework.
    /// </summary>
    public static bool IsSafeToRetry(HttpRequestMessage? request)
    {
        if (request is null)
        {
            return false;
        }

        if (IsIdempotentByHttpSpec(request.Method))
        {
            return true;
        }

        if (request.Options.TryGetValue(SafeToRetryOptionKey, out var explicitlyMarked) && explicitlyMarked)
        {
            return true;
        }

        return request.Headers.Contains(IdempotencyKeyHeaderName);
    }

    private static bool IsIdempotentByHttpSpec(HttpMethod method) =>
        method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Put
        || method == HttpMethod.Delete
        || method == HttpMethod.Options;

    /// <summary>
    /// Predicado de <c>HttpRetryStrategyOptions.ShouldHandle</c> que combina la condición de
    /// seguridad de esta clase con la detección estándar de fallas transitorias de
    /// <see cref="HttpClientResiliencePredicates.IsTransient(Outcome{HttpResponseMessage})"/> (5xx,
    /// 408, <see cref="HttpRequestException"/>, timeout de intento). Un request inseguro de
    /// reintentar nunca llega a evaluarse contra la transitoriedad de la falla: aunque el servicio
    /// externo responda 503, un <c>POST</c> sin marcador de idempotencia se deja tal cual, en vez de
    /// arriesgar una duplicación.
    /// </summary>
    internal static Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>> CreateShouldHandlePredicate() =>
        static args =>
        {
            var request = args.Context.GetRequestMessage();
            if (!IsSafeToRetry(request))
            {
                return new ValueTask<bool>(false);
            }

            return new ValueTask<bool>(HttpClientResiliencePredicates.IsTransient(args.Outcome));
        };
}
