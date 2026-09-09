namespace BitCode.Framework.Platform.Documents.Almacenamiento;

/// <summary>
/// Abstracción de almacenamiento de blobs DESACOPLADA (Épica de Documents: "Integración con
/// almacenamiento desacoplado") -- ningún handler ni endpoint de este módulo conoce el mecanismo físico
/// real (filesystem, Azure Blob Storage, S3, MinIO, etc.), solo esta interfaz. El repositorio NO tiene
/// hoy un módulo <c>Shared.Infrastructure.Storage</c> genérico (verificado con
/// <c>Glob src/Shared.Infrastructure.*</c> antes de esta tarea) -- por eso esta abstracción nace acoplada
/// al módulo Documents en este primer corte, con una única implementación de referencia
/// (<see cref="FileSystemDocumentBlobStore"/>, filesystem local) suficiente para demostrar el flujo
/// completo de negocio y para las pruebas de integración reales (filesystem real, no un mock).
/// <para>
/// <b>Pendiente explícito (ver <c>docs/guia-documents.md</c>, sección "Pendientes"):</b> un adapter real
/// de Azure Blob Storage/S3/MinIO es una decisión de infraestructura de producción (endpoint,
/// credenciales, cifrado en tránsito/reposo, política de acceso) que excede el alcance de este corte --
/// implementarlo hoy sería sobre-ingeniería sin un consumidor productivo real que lo necesite (Plan
/// Maestro, sección 3.2). Cuando exista ese consumidor, la nueva implementación solo necesita
/// implementar esta misma interfaz y registrarse en el contenedor de DI -- ningún handler de este módulo
/// cambia.
/// </para>
/// </summary>
public interface IDocumentBlobStore
{
    /// <summary>
    /// Escribe <paramref name="content"/> bajo <paramref name="blobKey"/>. <paramref name="blobKey"/> es
    /// una clave opaca elegida por el llamador (ver <c>BlobKeyFactory</c>) -- nunca una ruta de
    /// filesystem cruda que el llamador ensambla por su cuenta.
    /// </summary>
    Task UploadAsync(string blobKey, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abre <paramref name="blobKey"/> para lectura. Lanza <see cref="FileNotFoundException"/> si la
    /// clave no existe -- el llamador (siempre un handler de aplicación, nunca un endpoint directamente)
    /// es responsable de traducir esa excepción a un <c>Result.Failure</c> (ver
    /// <c>DescargarDocumentoVersionQueryHandler</c>, criterio de aceptación de recuperación del Gate de
    /// salida de Fase 6: "metadata presente, archivo físico ausente" nunca debe ensuciar al cliente con
    /// un 500 no controlado).
    /// </summary>
    Task<Stream> DownloadAsync(string blobKey, CancellationToken cancellationToken = default);

    /// <summary>Elimina el contenido bajo <paramref name="blobKey"/> -- no falla si la clave no existe
    /// (idempotente, mismo criterio que un DELETE HTTP).</summary>
    Task DeleteAsync(string blobKey, CancellationToken cancellationToken = default);

    /// <summary>Existencia física del contenido -- usado por las pruebas de recuperación del módulo, no
    /// por ningún handler de negocio en este primer corte.</summary>
    Task<bool> ExistsAsync(string blobKey, CancellationToken cancellationToken = default);
}
