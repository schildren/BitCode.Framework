namespace BitCode.Framework.Platform.ImportExport.Exportacion;

internal sealed record ExportJobResponse(
    Guid Id,
    string TipoExportacion,
    ExportJobEstado Estado,
    int? FilasTotales,
    int FilasExportadas,
    int FilasConError,
    string? ErrorMensaje,
    DateTime? FinalizadoAtUtc);

internal sealed record ExportJobDescargaResponse(byte[] Contenido, string ContentType, string NombreArchivo);
