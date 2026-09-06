using BitCode.Framework.Shared.Domain.Idempotency;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

/// <summary>
/// Sustituto de <see cref="IIdempotencyKeyProvider"/> para tests de integración (F1-22): a diferencia
/// de <c>HttpContextIdempotencyKeyProvider</c> (que lee un header HTTP real), esta implementación
/// mutable permite fijar la clave a usar en cada <c>ISender.Send</c> sin necesidad de un
/// <c>HttpContext</c>, registrada como singleton (misma instancia) para poder cambiar
/// <see cref="IdempotencyKey"/> entre llamadas dentro del mismo <c>IServiceProvider</c>/scope.
/// </summary>
public class FakeIdempotencyKeyProvider : IIdempotencyKeyProvider
{
    public string? IdempotencyKey { get; set; }
}
