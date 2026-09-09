using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.ImportExport.Importacion;

/// <summary>
/// Un error de UNA fila puntual de un <see cref="ImportJob"/> (Fase 6, módulo 10: "errores" del Plan
/// Maestro) -- append-only, nunca se actualiza ni se borra una fila ya escrita, mismo espíritu que
/// <c>IntegrationRequestLog</c> (Fase 6, módulo 9).
/// </summary>
public sealed class ImportJobError : Entity<Guid>, ITenantEntity
{
    public Guid ImportJobId { get; private set; }

    /// <summary>1-based, sin contar el encabezado -- la primera fila de datos es la fila 1.</summary>
    public int NumeroFila { get; private set; }

    public string MensajeError { get; private set; } = string.Empty;

    /// <summary>Contenido crudo de la línea que produjo el error, truncado a 2000 caracteres -- para
    /// diagnóstico, nunca reprocesado automáticamente por este módulo.</summary>
    public string ContenidoFilaCrudo { get; private set; } = string.Empty;

    public Guid TenantId { get; set; }

    public ImportJobError(Guid id, Guid importJobId, int numeroFila, string mensajeError, string contenidoFilaCrudo)
        : base(id)
    {
        ImportJobId = importJobId;
        NumeroFila = numeroFila;
        MensajeError = mensajeError;
        ContenidoFilaCrudo = contenidoFilaCrudo.Length > 2000 ? contenidoFilaCrudo[..2000] : contenidoFilaCrudo;
    }

    private ImportJobError()
    {
    }
}
