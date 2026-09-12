namespace BitCode.Framework.Platform.ImportExport.Almacenamiento;

/// <summary>
/// Abstracción de almacenamiento TRANSITORIO propia de este módulo (Fase 6, módulo 10) -- ver
/// <c>docs/guia-import-export.md</c>, sección "Relación con Documents", para la decisión honesta de por
/// qué este módulo NO reutiliza <c>IDocumentBlobStore</c>/<c>FileSystemDocumentBlobStore</c> de Documents
/// (Fase 6, módulo 5) pese a que el Plan Maestro declara "Documents" como dependencia de este módulo.
/// Resumen: agregar una referencia de proyecto a <c>BitCode.Platform.Documents</c> solo para reutilizar una
/// clase de almacenamiento de OTRO bounded context acopla dos módulos que deberían poder evolucionar
/// (incluso desplegarse) independientemente -- esta interfaz es deliberadamente más chica que
/// <c>IDocumentBlobStore</c> (sin versión ni antivirus ni metadata de documento) porque acá el archivo es
/// transitorio: se sube una vez, se lee en chunks sucesivos mientras el job avanza, y no tiene ningún
/// ciclo de vida de negocio propio más allá del <c>ImportJob</c>/<c>ExportJob</c> que lo referencia.
/// </summary>
public interface IImportExportFileStore
{
    /// <summary>Escribe (o sobrescribe atómicamente) el contenido completo bajo <paramref name="blobKey"/>
    /// -- usado para la carga inicial de un archivo a importar y para el primer chunk de un archivo de
    /// exportación (que crea el archivo de resultado con su encabezado).</summary>
    Task UploadAsync(string blobKey, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Agrega <paramref name="contenido"/> al final del archivo bajo <paramref name="blobKey"/>
    /// (lo crea si no existe) -- usado por <c>Procesamiento.ExportBatchProcessorJob</c> para ir escribiendo
    /// cada chunk exportado sin mantener el archivo completo en memoria. Ver
    /// <c>docs/guia-import-export.md</c>, sección "Reanudación de una exportación", para la limitación
    /// honesta de esta operación frente a una caída del proceso a mitad de un chunk.</summary>
    Task AppendAsync(string blobKey, string contenido, CancellationToken cancellationToken = default);

    /// <summary>Abre <paramref name="blobKey"/> para lectura. Lanza <see cref="FileNotFoundException"/> si
    /// la clave no existe -- el llamador es responsable de traducir esa excepción a un fallo clasificado
    /// (ver <c>Procesamiento.ImportBatchProcessorJob</c>: un archivo fuente eliminado entre el alta del
    /// <c>ImportJob</c> y su procesamiento es un fallo PERMANENTE del job, nunca una excepción no
    /// controlada).</summary>
    Task<Stream> OpenReadAsync(string blobKey, CancellationToken cancellationToken = default);

    /// <summary>Elimina el contenido bajo <paramref name="blobKey"/> -- no falla si la clave no existe
    /// (idempotente, mismo criterio que un DELETE HTTP).</summary>
    Task DeleteAsync(string blobKey, CancellationToken cancellationToken = default);
}
