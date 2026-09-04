namespace BitCode.Framework.Shared.Kernel;

public abstract record DomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
