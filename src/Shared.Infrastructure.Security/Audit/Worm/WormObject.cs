namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

/// <summary>Resultado de <see cref="IWormStorage.ReadAsync"/>: metadata más el contenido íntegro (F2-18).</summary>
public sealed class WormObject
{
    public WormObject(WormObjectMetadata metadata, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(content);

        Metadata = metadata;
        Content = content;
    }

    public WormObjectMetadata Metadata { get; }

    public byte[] Content { get; }
}
