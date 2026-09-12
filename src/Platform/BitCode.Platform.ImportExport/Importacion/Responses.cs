namespace BitCode.Framework.Platform.ImportExport.Importacion;

internal sealed record ImportJobResponse(
    Guid Id,
    string TipoImportacion,
    string NombreArchivoOriginal,
    ImportJobEstado Estado,
    int FilasTotales,
    int FilasProcesadas,
    int FilasConError,
    string? ErrorMensaje,
    DateTime? FinalizadoAtUtc);

internal sealed record ImportJobErrorResponse(Guid Id, int NumeroFila, string MensajeError, string ContenidoFilaCrudo);
