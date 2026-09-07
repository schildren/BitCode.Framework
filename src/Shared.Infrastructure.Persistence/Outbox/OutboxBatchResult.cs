namespace BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;

/// <summary>Resultado de un único ciclo de <c>OutboxBatchProcessor.ProcessBatchAsync</c> (F3-03).</summary>
public sealed record OutboxBatchResult(int Claimed, int Published, int SkippedInternal, int Failed)
{
    public static readonly OutboxBatchResult Empty = new(0, 0, 0, 0);
}
