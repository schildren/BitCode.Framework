namespace BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;

/// <summary>Resultado de un único ciclo de <c>OutboxBatchProcessor.ProcessBatchAsync</c> (F3-03/F3-07).</summary>
/// <param name="Claimed">Filas reclamadas en este ciclo.</param>
/// <param name="Published">Filas publicadas con éxito.</param>
/// <param name="SkippedInternal">Filas de <c>DomainEvent</c> puramente interno, marcadas procesadas sin publicar.</param>
/// <param name="Failed">
/// Filas cuyo intento de este ciclo falló (incluye tanto las que quedan pendientes de reintento como
/// las que además quedaron <see cref="Exhausted"/> en este mismo ciclo — <see cref="Exhausted"/> es un
/// subconjunto de <see cref="Failed"/>, no una categoría separada).
/// </param>
/// <param name="Exhausted">
/// F3-07: de las filas <see cref="Failed"/> en este ciclo, cuántas superaron
/// <c>EventRetryPolicyOptions.MaxAttempts</c> (o fueron clasificadas como error permanente) y quedaron
/// marcadas <c>OutboxMessage.ExhaustedAtUtc</c> — ya no se reintentan en ningún ciclo futuro.
/// </param>
public sealed record OutboxBatchResult(int Claimed, int Published, int SkippedInternal, int Failed, int Exhausted = 0)
{
    public static readonly OutboxBatchResult Empty = new(0, 0, 0, 0, 0);
}
