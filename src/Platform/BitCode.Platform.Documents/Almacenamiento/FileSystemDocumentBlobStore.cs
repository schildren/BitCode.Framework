using Microsoft.Extensions.Options;

namespace BitCode.Framework.Platform.Documents.Almacenamiento;

/// <summary>
/// Implementación de referencia de <see cref="IDocumentBlobStore"/> sobre el filesystem local -- mismo
/// espíritu que el vault local de backups de F5-07/F5-09 (<c>tools/SqlBackupAutomation</c>): un directorio
/// raíz configurable (<see cref="DocumentBlobStoreOptions.RootPath"/>) con un archivo por
/// <c>blobKey</c>. Suficiente para demostrar el flujo de negocio completo y para pruebas de integración
/// contra un filesystem real (no un mock) -- un adapter real de object storage en la nube es un pendiente
/// explícito, ver <see cref="IDocumentBlobStore"/>.
/// </summary>
internal sealed class FileSystemDocumentBlobStore(IOptions<DocumentBlobStoreOptions> options) : IDocumentBlobStore
{
    public async Task UploadAsync(string blobKey, Stream content, CancellationToken cancellationToken = default)
    {
        var finalPath = ResolvePath(blobKey);
        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);

        // Escritura atómica (mismo criterio que un backup WORM, F5-09): se escribe primero a un archivo
        // temporal en el mismo volumen y recién se mueve al destino final al terminar -- un consumidor
        // que intente descargar mientras la subida está en curso nunca ve un archivo parcial/corrupto.
        var tempPath = $"{finalPath}.{Guid.NewGuid():N}.tmp";
        await using (var fileStream = new FileStream(
            tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        File.Move(tempPath, finalPath, overwrite: true);
    }

    public Task<Stream> DownloadAsync(string blobKey, CancellationToken cancellationToken = default)
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

    public Task<bool> ExistsAsync(string blobKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(ResolvePath(blobKey)));

    /// <summary>
    /// Normaliza <paramref name="blobKey"/> a una ruta dentro de <see cref="DocumentBlobStoreOptions.RootPath"/>
    /// -- rechaza segmentos <c>".."</c> para que un <c>blobKey</c> nunca pueda escapar del directorio raíz
    /// (path traversal), aunque en este primer corte <c>blobKey</c> siempre lo genera
    /// <c>BlobKeyFactory</c> a partir de GUIDs propios, nunca de un valor provisto por el cliente HTTP.
    /// </summary>
    private string ResolvePath(string blobKey)
    {
        if (string.IsNullOrWhiteSpace(options.Value.RootPath))
        {
            throw new InvalidOperationException(
                "Falta configurar Documents:BlobStore:RootPath (DocumentBlobStoreOptions.RootPath).");
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
