namespace BitCode.Framework.Shared.Domain.Idempotency;

/// <summary>
/// Expone la Idempotency-Key del request/scope actual (F1-22), sin importar de dónde viene
/// (típicamente el header HTTP <c>Idempotency-Key</c>, ver <see cref="IdempotencyHeaderNames"/>).
/// Mismo patrón que <see cref="BitCode.Framework.Shared.Domain.MultiTenancy.ITenantProvider"/>/
/// <see cref="BitCode.Framework.Shared.Domain.Security.ICurrentUserProvider"/>: un contrato de bajo
/// nivel en Shared.Domain que <c>Shared.Application</c> (<c>IdempotencyBehavior</c>) consume sin
/// depender de ASP.NET Core, con una implementación no-op por defecto y una implementación
/// productiva registrada por el proyecto web consumidor.
/// </summary>
public interface IIdempotencyKeyProvider
{
    /// <summary>
    /// Valor de la Idempotency-Key del request actual, o <see langword="null"/> si el cliente no
    /// envió ninguna. <see langword="null"/> nunca implica "sin restricción": un comando que
    /// implementa <c>IIdempotentCommand</c> exige una clave no vacía (ver <c>IdempotencyBehavior</c>).
    /// </summary>
    string? IdempotencyKey { get; }
}
