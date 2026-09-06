using BitCode.Framework.Shared.Domain.Idempotency;

namespace BitCode.Framework.Shared.Application.Idempotency;

/// <summary>
/// Implementación por defecto de <see cref="IIdempotencyKeyProvider"/> (F1-22), registrada por
/// <c>AddSharedApplication</c> con <c>TryAddScoped</c> — mismo patrón que
/// <c>NullTenantProvider</c>/<c>NullCurrentUserProvider</c>. Un proyecto con pipeline HTTP reemplaza
/// esta implementación por <c>HttpContextIdempotencyKeyProvider</c> (Shared.Infrastructure.Web)
/// llamando <c>AddHttpContextIdempotencyKeyProvider()</c> ANTES de <c>AddSharedApplication</c>. Sin
/// reemplazo, cualquier comando <c>IIdempotentCommand</c> siempre recibe <see langword="null"/> como
/// clave y <c>IdempotencyBehavior</c> lo rechaza con <c>IdempotencyErrors.KeyRequired</c> — nunca
/// ejecuta el handler sin una clave real.
/// </summary>
public sealed class NullIdempotencyKeyProvider : IIdempotencyKeyProvider
{
    public string? IdempotencyKey => null;
}
