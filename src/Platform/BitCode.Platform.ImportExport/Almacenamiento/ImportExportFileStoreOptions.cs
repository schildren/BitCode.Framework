namespace BitCode.Framework.Platform.ImportExport.Almacenamiento;

/// <summary>Configuración de <see cref="FileSystemImportExportFileStore"/> -- mismo espíritu que
/// <c>DocumentBlobStoreOptions</c> (Fase 6, módulo 5), pero un directorio raíz propio y separado: los
/// archivos de este módulo son transitorios y no deben mezclarse con el almacenamiento de documentos de
/// negocio de Documents.</summary>
public sealed class ImportExportFileStoreOptions
{
    public string RootPath { get; set; } = string.Empty;
}
