using BitCode.Framework.Shared.Domain.Idempotency;
using Microsoft.AspNetCore.Http;

namespace BitCode.Framework.Shared.Infrastructure.Web.Idempotency;

/// <summary>
/// Implementación productiva de <see cref="IIdempotencyKeyProvider"/> (F1-22): lee el header
/// <see cref="IdempotencyHeaderNames.IdempotencyKey"/> directamente del request. A diferencia de
/// <c>HttpContextTenantProvider</c> (que nunca confía en un dato controlado por el cliente para
/// resolver el <c>TenantId</c>, ver regla dura 12 de <c>docs/convenciones.md</c>), leer la
/// Idempotency-Key de un header es intencional y seguro: el valor no autoriza nada por sí mismo —
/// solo identifica un reintento del mismo cliente— y <c>IdempotencyBehavior</c> (Shared.Application)
/// además exige que el hash del payload coincida antes de reutilizar cualquier resultado guardado
/// bajo esa clave, así que un valor adivinado por un tercero no permite leer ni reutilizar el
/// resultado de otro cliente salvo que también conozca el payload exacto.
/// </summary>
public sealed class HttpContextIdempotencyKeyProvider(IHttpContextAccessor httpContextAccessor)
    : IIdempotencyKeyProvider
{
    public string? IdempotencyKey =>
        httpContextAccessor.HttpContext?.Request.Headers[IdempotencyHeaderNames.IdempotencyKey] is { Count: > 0 } values
            ? values[0]
            : null;
}
