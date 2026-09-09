namespace BitCode.Framework.Platform.ImportExport;

/// <summary>Catálogo de permisos RBAC (F2-07) de este módulo -- ver <c>docs/guia-import-export.md</c> para
/// la matriz permiso/endpoint. Igual que Integration Hub (Fase 6, módulo 9), ninguna operación de este
/// módulo tiene un control de ownership adicional además del permiso: un <c>ImportJob</c>/<c>ExportJob</c>
/// es un dato operacional de una operación masiva sobre datos de negocio, no un dato personal de un
/// usuario final (ver el <c>remarks</c> de <c>Importacion.ObtenerImportJobQuery</c>).</summary>
public static class ImportExportPermissions
{
    public const string ImportacionesIniciar = "importexport.importaciones.iniciar";
    public const string ImportacionesVer = "importexport.importaciones.ver";
    public const string ExportacionesIniciar = "importexport.exportaciones.iniciar";
    public const string ExportacionesVer = "importexport.exportaciones.ver";
}
