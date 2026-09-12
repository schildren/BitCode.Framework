using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.ImportExport.Exportacion;

/// <summary>Un error de UNA fila puntual de un <see cref="ExportJob"/> -- append-only, simétrico a
/// <c>Importacion.ImportJobError</c>.</summary>
public sealed class ExportJobError : Entity<Guid>, ITenantEntity
{
    public Guid ExportJobId { get; private set; }

    /// <summary>0-based: la posición de la fila dentro de la fuente de datos completa (el mismo índice que
    /// <see cref="ExportJob.UltimoOffsetExportado"/> usa como checkpoint), no la posición dentro del
    /// archivo de resultado (que puede diferir si otras filas del mismo chunk fallaron antes que
    /// esta).</summary>
    public int NumeroFila { get; private set; }

    public string MensajeError { get; private set; } = string.Empty;

    public Guid TenantId { get; set; }

    public ExportJobError(Guid id, Guid exportJobId, int numeroFila, string mensajeError)
        : base(id)
    {
        ExportJobId = exportJobId;
        NumeroFila = numeroFila;
        MensajeError = mensajeError;
    }

    private ExportJobError()
    {
    }
}
