namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

/// <summary>Resultado de <see cref="IAuditWormExportPipeline.ReadAsync"/> (F2-18, Épica F2-D).</summary>
public sealed class AuditWormExportedBatch
{
    public AuditWormExportedBatch(WormObjectMetadata metadata, IReadOnlyList<AuditEntry> batch, AuditBatchSignature? signature)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(batch);

        Metadata = metadata;
        Batch = batch;
        Signature = signature;
    }

    public WormObjectMetadata Metadata { get; }

    public IReadOnlyList<AuditEntry> Batch { get; }

    public AuditBatchSignature? Signature { get; }
}
