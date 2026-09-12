using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.ImportExport.Importacion;

/// <summary>
/// Un trabajo de importación por lotes (Fase 6, módulo 10: "validación, lotes, progreso, errores y
/// reanudación" del Plan Maestro). Se crea en <see cref="ImportJobEstado.Pendiente"/> con el archivo ya
/// guardado en <see cref="Almacenamiento.IImportExportFileStore"/> y <see cref="FilasTotales"/> ya
/// calculado (ver <c>Csv.CsvFileLineCounter</c>) -- <c>Procesamiento.ImportBatchProcessorJob</c>, corriendo
/// en ciclos posteriores, es quien realmente procesa el archivo en chunks sucesivos de
/// <see cref="ImportExportOptions.TamanoLoteFilas"/> filas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aislamiento por ítem desde el diseño inicial (no como corrección posterior):</b> una fila individual
/// con datos inválidos o que provoca una excepción no controlada en <see cref="IImportRowHandler"/> NUNCA
/// hace fallar el job completo -- se registra como un <see cref="ImportJobError"/> de ESA fila
/// (<see cref="RegistrarProgresoChunk"/> solo suma contadores) y el procesamiento continúa con la
/// siguiente. <see cref="MarcarFallido"/> es exclusivamente para fallos DEL JOB COMPLETO (archivo
/// inaccesible, tipo de importación sin handler registrado) -- nunca se llama por una fila individual.
/// </para>
/// <para>
/// <b>Reanudación (regla dura 28, idempotencia por diseño):</b> <see cref="UltimaFilaCheckpoint"/> se
/// persiste en el MISMO <c>SaveChangesAsync</c> que las filas de ese chunk -- si el proceso muere a mitad
/// de un chunk (antes de ese <c>SaveChangesAsync</c>), la próxima ejecución del job (Quartz
/// <c>RequestRecovery()</c>) vuelve a leer el archivo desde el último checkpoint CONFIRMADO, reprocesando
/// como máximo las filas del chunk interrumpido -- nunca pierde el progreso de chunks anteriores ya
/// confirmados. Un <see cref="IImportRowHandler"/> puede ser invocado más de una vez para la misma fila en
/// ese escenario (semántica "at-least-once", nunca exactly-once de punta a punta, Plan Maestro sección
/// 3.2) -- las implementaciones deben ser idempotentes (por ejemplo, upsert por clave natural) para que un
/// reproceso tras una caída no duplique el efecto de negocio.
/// </para>
/// <para>
/// <b>Sin <see cref="Shared.Kernel.IHasConcurrencyToken"/>:</b> a diferencia de <c>IntegrationRequest</c>/
/// <c>Notification</c>, esta fila tiene un ÚNICO escritor posible -- <c>ImportBatchProcessorJob</c>, y
/// Quartz en modo clustered (F4-11, <c>AddSharedBackgroundJobs</c>) garantiza que un mismo trigger no
/// corre concurrentemente en dos nodos. No existe hoy ningún comando que permita cancelar/editar un
/// <see cref="ImportJob"/> desde otro camino que pudiera competir con el job -- si un consumidor real
/// agrega esa operación en el futuro, en ESE momento corresponde agregar el token de concurrencia (ver
/// <c>docs/guia-import-export.md</c>, sección "Pendientes").
/// </para>
/// </remarks>
public sealed class ImportJob : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity
{
    public string TipoImportacion { get; private set; } = string.Empty;

    public string ArchivoBlobKey { get; private set; } = string.Empty;

    public string NombreArchivoOriginal { get; private set; } = string.Empty;

    public ImportJobEstado Estado { get; private set; } = ImportJobEstado.Pendiente;

    public int FilasTotales { get; private set; }

    public int FilasProcesadas { get; private set; }

    public int FilasConError { get; private set; }

    /// <summary>Cantidad de filas de datos ya procesadas y confirmadas (checkpoint de reanudación) -- ver
    /// el <c>remarks</c> de esta clase, sección "Reanudación".</summary>
    public int UltimaFilaCheckpoint { get; private set; }

    public Guid? IniciadoPorUserId { get; private set; }

    /// <summary>Motivo del fallo DEL JOB COMPLETO -- <see langword="null"/> salvo
    /// <see cref="ImportJobEstado.Fallido"/>. Nunca describe el error de una fila individual (ver
    /// <see cref="ImportJobError"/>).</summary>
    public string? ErrorMensaje { get; private set; }

    public DateTime? FinalizadoAtUtc { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public ImportJob(
        Guid id, string tipoImportacion, string archivoBlobKey, string nombreArchivoOriginal, int filasTotales,
        Guid? iniciadoPorUserId)
        : base(id)
    {
        TipoImportacion = tipoImportacion;
        ArchivoBlobKey = archivoBlobKey;
        NombreArchivoOriginal = nombreArchivoOriginal;
        FilasTotales = filasTotales;
        IniciadoPorUserId = iniciadoPorUserId;
    }

    private ImportJob()
    {
    }

    public void IniciarProcesamiento()
    {
        if (Estado == ImportJobEstado.Pendiente)
        {
            Estado = ImportJobEstado.EnProgreso;
        }
    }

    /// <summary>Registra el resultado de UN chunk ya procesado -- avanza el checkpoint exactamente la
    /// cantidad de filas leídas en ese chunk (incluidas las que resultaron en error, que sí se "procesaron"
    /// en el sentido de que no se van a reintentar solas).</summary>
    public void RegistrarProgresoChunk(int filasEnChunk, int erroresEnChunk, int nuevoCheckpoint)
    {
        FilasProcesadas += filasEnChunk;
        FilasConError += erroresEnChunk;
        UltimaFilaCheckpoint = nuevoCheckpoint;
    }

    public void MarcarCompletado(DateTime atUtc)
    {
        Estado = FilasConError > 0 ? ImportJobEstado.CompletadoConErrores : ImportJobEstado.Completado;
        FinalizadoAtUtc = atUtc;

        RaiseDomainEvent(new ImportacionCompletadaIntegrationEvent(Id, TipoImportacion, FilasTotales, FilasProcesadas, FilasConError));
    }

    public void MarcarFallido(string error, DateTime atUtc)
    {
        Estado = ImportJobEstado.Fallido;
        ErrorMensaje = error;
        FinalizadoAtUtc = atUtc;

        RaiseDomainEvent(new ImportacionFallidaIntegrationEvent(Id, TipoImportacion, error));
    }
}
