using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.ImportExport.Exportacion;

/// <summary>
/// Un trabajo de exportación por lotes (Fase 6, módulo 10: "lotes, progreso, errores y reanudación" del
/// Plan Maestro, simétrico a <see cref="Importacion.ImportJob"/>). Se crea en
/// <see cref="ExportJobEstado.Pendiente"/> SIN archivo todavía -- <c>Procesamiento.ExportBatchProcessorJob</c>
/// crea el archivo de resultado (con su encabezado) en el primer chunk y lo va completando en chunks
/// sucesivos de <see cref="ImportExportOptions.TamanoLoteFilas"/> filas, leídas de un
/// <see cref="IExportDataSource"/> pluggable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aislamiento por ítem:</b> una fila individual cuyo valor no puede serializarse a CSV (caso extremo,
/// ver <c>Csv.CsvLineParser.WriteField</c>) o cuya conversión provoca una excepción no controlada NUNCA
/// hace fallar el job completo -- se registra como un <see cref="ExportJobError"/> de esa fila y el
/// procesamiento continúa. <see cref="MarcarFallido"/> es exclusivamente para fallos DEL JOB COMPLETO (no
/// hay <see cref="IExportDataSource"/> registrado, o la fuente de datos lanza una excepción no controlada
/// al pedir una página completa) -- ese último caso SÍ es job-level porque sin la página no hay ninguna
/// fila individual sobre la que aislar el error.
/// </para>
/// <para>
/// <b>Reanudación -- limitación honesta, distinta de <see cref="Importacion.ImportJob"/>:</b>
/// <see cref="UltimoOffsetExportado"/> se persiste en el mismo <c>SaveChangesAsync</c> que el resto del
/// progreso del chunk, DESPUÉS de que <see cref="Almacenamiento.IImportExportFileStore.AppendAsync"/> ya
/// escribió ese chunk al archivo -- si el proceso muere ENTRE esas dos operaciones, el archivo de
/// resultado queda con las filas de ese chunk ya escritas pero el checkpoint todavía apunta antes de él;
/// la próxima ejecución vuelve a pedirle esas mismas filas a <see cref="IExportDataSource"/> y las
/// vuelve a anexar, duplicándolas en el archivo final. Documentado explícitamente como un pendiente (ver
/// <c>docs/guia-import-export.md</c>, sección "Reanudación de una exportación") -- corregirlo requeriría
/// un checkpoint de OFFSET DE BYTE dentro del archivo de resultado (truncar hasta ese offset antes de
/// reintentar) en vez de un conteo de filas, que esta primera versión no implementa. Nunca se promete
/// exactamente-una-vez de punta a punta (Plan Maestro, sección 3.2).
/// </para>
/// </remarks>
public sealed class ExportJob : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity
{
    public string TipoExportacion { get; private set; } = string.Empty;

    /// <summary>Filtro opcional, en JSON, que <see cref="IExportDataSource"/> interpreta a su criterio --
    /// este módulo nunca lo valida más allá de "es JSON bien formado" (ver el validador del comando de
    /// alta).</summary>
    public string? FiltroJson { get; private set; }

    public ExportJobEstado Estado { get; private set; } = ExportJobEstado.Pendiente;

    /// <summary><see langword="null"/> hasta que <see cref="IExportDataSource"/> informa un total conocido
    /// en alguna página -- algunas fuentes de datos no conocen el total por adelantado (por ejemplo, un
    /// cursor sobre un stream externo), en cuyo caso el progreso solo puede mostrar
    /// <see cref="FilasExportadas"/> sin porcentaje.</summary>
    public int? FilasTotales { get; private set; }

    public int FilasExportadas { get; private set; }

    public int FilasConError { get; private set; }

    /// <summary>Checkpoint de reanudación -- ver el <c>remarks</c> de esta clase para su limitación
    /// honesta frente al de <see cref="Importacion.ImportJob"/>.</summary>
    public int UltimoOffsetExportado { get; private set; }

    /// <summary>Clave de almacenamiento del archivo de resultado -- asignada al crear el job
    /// (determinística a partir de <see cref="Entity{TId}.Id"/>) para poder ir anexando chunks desde el
    /// primer ciclo, aunque el archivo físico recién se crea en ese primer ciclo.</summary>
    public string ArchivoResultadoBlobKey { get; private set; } = string.Empty;

    public Guid? IniciadoPorUserId { get; private set; }

    public string? ErrorMensaje { get; private set; }

    public DateTime? FinalizadoAtUtc { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public ExportJob(Guid id, string tipoExportacion, string? filtroJson, string archivoResultadoBlobKey, Guid? iniciadoPorUserId)
        : base(id)
    {
        TipoExportacion = tipoExportacion;
        FiltroJson = filtroJson;
        ArchivoResultadoBlobKey = archivoResultadoBlobKey;
        IniciadoPorUserId = iniciadoPorUserId;
    }

    private ExportJob()
    {
    }

    public void IniciarProcesamiento()
    {
        if (Estado == ExportJobEstado.Pendiente)
        {
            Estado = ExportJobEstado.EnProgreso;
        }
    }

    public void ActualizarTotalConocido(int filasTotales) => FilasTotales = filasTotales;

    public void RegistrarProgresoChunk(int filasExportadasEnChunk, int erroresEnChunk, int nuevoOffset)
    {
        FilasExportadas += filasExportadasEnChunk;
        FilasConError += erroresEnChunk;
        UltimoOffsetExportado = nuevoOffset;
    }

    public void MarcarCompletado(DateTime atUtc)
    {
        Estado = FilasConError > 0 ? ExportJobEstado.CompletadoConErrores : ExportJobEstado.Completado;
        FinalizadoAtUtc = atUtc;

        RaiseDomainEvent(new ExportacionCompletadaIntegrationEvent(Id, TipoExportacion, FilasExportadas, FilasConError));
    }

    public void MarcarFallido(string error, DateTime atUtc)
    {
        Estado = ExportJobEstado.Fallido;
        ErrorMensaje = error;
        FinalizadoAtUtc = atUtc;

        RaiseDomainEvent(new ExportacionFallidaIntegrationEvent(Id, TipoExportacion, error));
    }
}
