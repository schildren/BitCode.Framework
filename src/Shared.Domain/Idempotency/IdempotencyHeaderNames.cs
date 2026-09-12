namespace BitCode.Framework.Shared.Domain.Idempotency;

/// <summary>
/// Nombre del header HTTP estándar que un proyecto consumidor con pipeline web usa para transportar
/// la Idempotency-Key del cliente (F1-22). Ver <c>HttpContextIdempotencyKeyProvider</c>
/// (Shared.Infrastructure.Web).
/// </summary>
public static class IdempotencyHeaderNames
{
    public const string IdempotencyKey = "Idempotency-Key";
}
