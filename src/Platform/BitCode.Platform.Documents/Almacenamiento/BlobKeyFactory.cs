namespace BitCode.Framework.Platform.Documents.Almacenamiento;

/// <summary>Genera la clave opaca (<c>blobKey</c>) de <see cref="IDocumentBlobStore"/> a partir de
/// identificadores propios del módulo -- nunca a partir de un valor provisto por el cliente HTTP (mismo
/// motivo por el que <see cref="FileSystemDocumentBlobStore"/> puede confiar en que un <c>blobKey</c>
/// nunca contiene un intento de path traversal deliberado).</summary>
internal static class BlobKeyFactory
{
    public static string Crear(Guid documentoId, Guid versionId) => $"{documentoId:N}/{versionId:N}.bin";
}
