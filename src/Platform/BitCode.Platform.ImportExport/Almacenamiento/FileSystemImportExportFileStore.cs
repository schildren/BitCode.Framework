using Microsoft.Extensions.Options;

namespace BitCode.Framework.Platform.ImportExport.Almacenamiento;

/// <summary>
/// Implementación de referencia de <see cref="IImportExportFileStore"/> sobre el filesystem local -- mismo
/// espíritu que <c>FileSystemDocumentBlobStore</c> (Fase 6, módulo 5): protección de path traversal y
/// escritura atómica para <see cref="UploadAsync"/>, pero una implementación PROPIA e independiente (ver el
/// <c>remarks</c> de <see cref="IImportExportFileStore"/> para por qué no se reutiliza la de Documents).
/// </summary>
internal sealed class FileSystemImportExportFileStore(IOptions<ImportExportFileStoreOptions> options) : IImportExportFileStore
{
    public async Task UploadAsync(string blobKey, Stream content, CancellationToken cancellationToken = default)
    {
        var finalPath = ResolvePath(blobKey);
        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);

        // Escritura atómica (mismo criterio que FileSystemDocumentBlobStore/backup WORM F5-09): se escribe
        // primero a un archivo temporal en el mismo volumen y recién se mueve al destino final al terminar.
        var tempPath = $"{finalPath}.{Guid.NewGuid():N}.tmp";
        await using (var fileStream = new FileStream(
            tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        File.Move(tempPath, finalPath, overwrite: true);
    }

    public async Task AppendAsync(string blobKey, string contenido, CancellationToken cancellationToken = default)
    {
        var finalPath = ResolvePath(blobKey);
        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);

        await using var fileStream = new FileStream(
            finalPath, FileMode.Append, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
        await using var writer = new StreamWriter(fileStream);
        await writer.WriteAsync(contenido.AsMemory(), cancellationToken);
    }

    public Task<Stream> OpenReadAsync(string blobKey, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(blobKey);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No existe contenido físico para blobKey '{blobKey}'.", path);
        }

        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string blobKey, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(blobKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>Normaliza <paramref name="blobKey"/> a una ruta dentro de
    /// <see cref="ImportExportFileStoreOptions.RootPath"/> -- rechaza segmentos <c>".."</c> para que un
    /// <c>blobKey</c> nunca pueda escapar del directorio raíz (path traversal), mismo criterio que
    /// <c>FileSystemDocumentBlobStore.ResolvePath</c>. <c>blobKey</c> siempre lo genera este módulo a partir
    /// de GUIDs propios (ver <c>Importacion.ImportJob</c>/<c>Exportacion.ExportJob</c>), nunca un valor
    /// provisto directamente por el cliente HTTP.</summary>
    private string ResolvePath(string blobKey)
    {
        if (string.IsNullOrWhiteSpace(options.Value.RootPath))
        {
            throw new InvalidOperationException(
                "Falta configurar ImportExport:FileStore:RootPath (ImportExportFileStoreOptions.RootPath).");
        }

        var segments = blobKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".."))
        {
            throw new ArgumentException($"blobKey inválido: '{blobKey}'.", nameof(blobKey));
        }

        var root = Path.GetFullPath(options.Value.RootPath);
        var combined = Path.GetFullPath(Path.Combine([root, .. segments]));
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"blobKey inválido: '{blobKey}'.", nameof(blobKey));
        }

        return combined;
    }
}
