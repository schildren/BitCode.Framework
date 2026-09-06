using BitCode.Framework.Shared.Kernel;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

/// <summary>Implementación por defecto de <see cref="IAuditWormExportPipeline"/> (F2-18, Épica F2-D).</summary>
public sealed class AuditWormExportPipeline : IAuditWormExportPipeline
{
    private readonly IWormStorage _wormStorage;
    private readonly IOptions<AuditWormExportOptions> _options;

    public AuditWormExportPipeline(IWormStorage wormStorage, IOptions<AuditWormExportOptions> options)
    {
        _wormStorage = wormStorage;
        _options = options;
    }

    public async Task<Result<WormObjectMetadata>> ExportAsync(
        AuditWormExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Batch.Count == 0)
        {
            return Result.Failure<WormObjectMetadata>(Error.Validation(
                "AuditWormExport.EmptyBatch", "No se puede exportar un lote de auditoría vacío al almacenamiento WORM."));
        }

        var content = AuditWormBatchSerializer.Serialize(request.Batch, request.Signature);
        var retentionPeriod = request.RetentionPeriod ?? _options.Value.RetentionPeriod;

        return await _wormStorage.WriteAsync(
            new WormWriteRequest(request.Key, content, retentionPeriod), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditWormExportedBatch>> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var readResult = await _wormStorage.ReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (readResult.IsFailure)
        {
            return Result.Failure<AuditWormExportedBatch>(readResult.Error);
        }

        try
        {
            var (batch, signature) = AuditWormBatchSerializer.Deserialize(readResult.Value.Content);
            return Result.Success(new AuditWormExportedBatch(readResult.Value.Metadata, batch, signature));
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<AuditWormExportedBatch>(Error.Failure(
                "AuditWormExport.DeserializationFailed",
                $"El contenido exportado con la clave '{key}' no pudo deserializarse: {ex.Message}"));
        }
    }
}
