using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BitCode.Framework.Shared.Application.Idempotency;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Idempotency;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Shared.Application.Behaviors;

/// <summary>
/// Da comportamiento real al marcador <see cref="IIdempotentCommand"/> (F1-22): un <c>POST</c>
/// repetido con la misma Idempotency-Key y el mismo payload no vuelve a ejecutar el handler, devuelve
/// el mismo resultado ya obtenido, y un reintento con la misma clave pero un payload distinto se
/// rechaza en vez de tratarse como la misma operación.
/// </summary>
/// <remarks>
/// Posición en el pipeline (F1-22): registrado en <c>AddSharedApplication</c> DESPUÉS de
/// <see cref="TransactionBehavior{TRequest,TResponse}"/> (Logging -&gt; Validation -&gt; Transaction
/// -&gt; Idempotency -&gt; Handler), es decir, este behavior corre DENTRO del alcance transaccional
/// de <c>TransactionBehavior</c>, no antes. Esto es deliberado: para que la inserción del nuevo
/// registro de idempotencia quede en el MISMO <c>SaveChangesAsync</c>/transacción física que el
/// efecto del comando (un fallo a mitad de camino nunca debe dejar el registro sin el efecto real, ni
/// al revés), la llamada a <c>IIdempotencyStore.Add</c> tiene que ocurrir ANTES de que
/// <c>TransactionBehavior</c> confirme (`CommitAsync`/`SaveChangesAsync`) — algo que solo es posible
/// si este behavior corre más adentro en el pipeline que `TransactionBehavior`, agregando el registro
/// al mismo <c>ChangeTracker</c> antes de devolver el control hacia afuera.
/// Costo aceptado: para un comando que además implementa <c>ITransactionalCommand</c>, un "cache hit"
/// (misma clave, mismo payload) ocurre con la transacción explícita ya abierta por
/// <c>TransactionBehavior</c> (en vez de cortocircuitar antes de abrirla) — un caso raro (combinar
/// ambos marcadores) y de costo bajo (abrir/confirmar una transacción sin escrituras). Para el caso
/// común (<c>IIdempotentCommand</c> simple, sin <c>ITransactionalCommand</c>), <c>TransactionBehavior</c>
/// no abre ninguna transacción explícita antes de este behavior, así que un cache hit no paga ningún
/// costo transaccional adicional.
/// </remarks>
public class IdempotencyBehavior<TRequest, TResponse>(
    IIdempotencyKeyProvider idempotencyKeyProvider,
    IIdempotencyStore idempotencyStore,
    IdempotencyOptions options,
    ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IIdempotentCommand, IRequest<TResponse>
    where TResponse : Result
{
    private static readonly Type? ResponseValueType = typeof(TResponse).IsGenericType
        ? typeof(TResponse).GetGenericArguments()[0]
        : null;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = idempotencyKeyProvider.IdempotencyKey;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            logger.LogWarning(
                "{RequestName} implementa IIdempotentCommand pero no se recibió ninguna Idempotency-Key",
                typeof(TRequest).Name);
            return CreateFailureResult<TResponse>(IdempotencyErrors.KeyRequired);
        }

        var requestHash = ComputeRequestHash(request);
        var existing = await idempotencyStore.FindAsync(idempotencyKey, cancellationToken);

        if (existing is not null && existing.ExpiresAtUtc <= DateTime.UtcNow)
        {
            // Expiración (F1-22): una entrada vencida se trata como inexistente a los fines de esta
            // ejecución, pero la fila física se descarta explícitamente antes de insertar la nueva
            // para no violar el índice único (TenantId, Key) de IdempotencyModelConfigurator.
            idempotencyStore.Remove(existing);
            existing = null;
        }

        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "La Idempotency-Key {IdempotencyKey} se reenvió con un payload distinto para {RequestName}",
                    idempotencyKey,
                    typeof(TRequest).Name);
                return CreateFailureResult<TResponse>(IdempotencyErrors.KeyReused);
            }

            logger.LogInformation(
                "La Idempotency-Key {IdempotencyKey} ya tiene un resultado guardado para {RequestName}; se " +
                "devuelve sin re-ejecutar el handler",
                idempotencyKey,
                typeof(TRequest).Name);
            return BuildCachedResponse(existing);
        }

        var response = await next();

        if (response.IsSuccess)
        {
            var utcNow = DateTime.UtcNow;
            idempotencyStore.Add(new IdempotencyKey
            {
                Id = Guid.NewGuid(),
                Key = idempotencyKey,
                RequestHash = requestHash,
                ResponseValueJson = SerializeResponseValue(response),
                CreatedAtUtc = utcNow,
                ExpiresAtUtc = utcNow.Add(options.RetentionPeriod),
            });
        }

        return response;
    }

    private static string ComputeRequestHash(TRequest request)
    {
        var json = JsonSerializer.Serialize(request, typeof(TRequest));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }

    private static string? SerializeResponseValue(TResponse response) =>
        ResponseValueType is null
            ? null
            : JsonSerializer.Serialize(
                typeof(TResponse).GetProperty(nameof(Result<object>.Value))!.GetValue(response),
                ResponseValueType);

    /// <summary>
    /// Reconstruye el <typeparamref name="TResponse"/> exitoso a partir de lo guardado (mismo patrón
    /// de reflexión que <see cref="TransactionBehavior{TRequest,TResponse}"/>/
    /// <see cref="ValidationBehavior{TRequest,TResponse}"/> para soportar tanto <c>Result</c> como
    /// <c>Result&lt;TValue&gt;</c> sin duplicar el behavior por tipo de retorno). Solo se cachean
    /// resultados exitosos (ver <see cref="IdempotencyKey.ResponseValueJson"/>), así que esta
    /// reconstrucción siempre produce un <c>Result</c>/<c>Result&lt;TValue&gt;</c> exitoso.
    /// </summary>
    private static TResponse BuildCachedResponse(IdempotencyKey record)
    {
        if (ResponseValueType is null)
        {
            return (TResponse)(object)Result.Success();
        }

        var value = JsonSerializer.Deserialize(record.ResponseValueJson!, ResponseValueType);
        var successMethod = typeof(Result)
            .GetMethods()
            .Single(m => m.Name == nameof(Result.Success) && m.IsGenericMethod)
            .MakeGenericMethod(ResponseValueType);

        return (TResponse)successMethod.Invoke(null, [value])!;
    }

    private static TResult CreateFailureResult<TResult>(Error error)
        where TResult : Result
    {
        if (typeof(TResult) == typeof(Result))
        {
            return (TResult)(object)Result.Failure(error);
        }

        var valueType = typeof(TResult).GetGenericArguments()[0];

        var failureMethod = typeof(Result)
            .GetMethods()
            .Single(m => m.Name == nameof(Result.Failure) && m.IsGenericMethod)
            .MakeGenericMethod(valueType);

        return (TResult)failureMethod.Invoke(null, [error])!;
    }
}
