namespace BitCode.Framework.Platform.Documents.Almacenamiento;

/// <summary>Opciones de <see cref="FileSystemDocumentBlobStore"/> -- sección de configuración
/// <c>"Documents:BlobStore"</c>.</summary>
public sealed class DocumentBlobStoreOptions
{
    /// <summary>Directorio raíz donde <see cref="FileSystemDocumentBlobStore"/> escribe el contenido de
    /// cada versión, uno por <c>blobKey</c> (subcarpetas incluidas). Debe ser un disco con las mismas
    /// garantías de durabilidad que la base de datos del host consumidor en producción -- este primer
    /// corte no impone ninguna (ver <c>docs/guia-documents.md</c>, sección "Pendientes").</summary>
    public string RootPath { get; set; } = string.Empty;
}
