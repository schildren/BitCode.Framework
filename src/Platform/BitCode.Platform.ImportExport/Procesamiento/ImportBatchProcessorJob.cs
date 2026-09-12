using BitCode.Framework.Platform.ImportExport.Almacenamiento;
using BitCode.Framework.Platform.ImportExport.Csv;
using BitCode.Framework.Platform.ImportExport.Importacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace BitCode.Framework.Platform.ImportExport.Procesamiento;

/// <summary>
/// Procesa, en cada disparo, UN chunk de cada <see cref="ImportJob"/> pendiente
/// (<see cref="ImportJobEstado.Pendiente"/>/<see cref="ImportJobEstado.EnProgreso"/>) -- Fase 6, módulo 10:
/// "lotes" del Plan Maestro. Un consumidor real lo registra con <c>AddSharedBackgroundJobs</c> (F4-11,
/// Quartz HA), mismo patrón exacto que <c>NotificationRetryJob</c>/<c>IntegrationOutboundProcessorJob</c>:
/// <code>
/// services.AddSharedBackgroundJobs(
///     quartz =>
///     {
///         var jobKey = new JobKey("importexport-import-batch");
///         quartz.AddJob&lt;ImportBatchProcessorJob&gt;(j => j.WithIdentity(jobKey).RequestRecovery().StoreDurably());
///         quartz.AddTrigger(t => t.ForJob(jobKey).WithSimpleSchedule(s => s.WithIntervalInSeconds(5).RepeatForever()));
///     },
///     ha => ha.ConnectionString = connectionString);
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Acceso directo a <see cref="ImportExportDbContext"/> con <c>IgnoreQueryFilters()</c> -- excepción
/// legítima documentada a la regla dura 1/5 (docs/convenciones.md), mismo precedente que
/// <c>IntegrationOutboundProcessorJob</c>: este job es un worker de infraestructura, no un handler de
/// comando dentro del pipeline de MediatR/<c>TransactionBehavior</c>, y necesita ver <see cref="ImportJob"/>
/// pendientes de TODOS los tenants en cada disparo. Llama <c>SaveChangesAsync</c> explícitamente por la
/// misma razón.
/// </para>
/// <para>
/// <b>Aislamiento por ítem, en DOS niveles:</b> (1) un <see cref="ImportJob"/> completo con una excepción no
/// controlada al procesarlo (por ejemplo, el archivo desapareció del filesystem) nunca aborta el resto del
/// lote de jobs pendientes -- se marca <see cref="ImportJob.MarcarFallido"/> y el <c>foreach</c> de jobs
/// sigue. (2) DENTRO de un job, una fila individual con datos inválidos o que hace que
/// <see cref="IImportRowHandler"/> lance una excepción no controlada NUNCA aborta el resto del chunk -- se
/// registra un <see cref="ImportJobError"/> de esa fila y el <c>while</c> de filas sigue. Ambos niveles
/// aplicados desde el diseño inicial (Fase 6, módulo 9 ya estableció este criterio, no como corrección
/// posterior a una auditoría).
/// </para>
/// <para>
/// <b>Reanudación (regla dura 28):</b> ver el <c>remarks</c> de <see cref="ImportJob"/>, sección
/// "Reanudación", y la limitación de performance documentada en <c>Csv.CsvFileLineCounter</c>/esta clase:
/// para saltar las filas ya procesadas, este método vuelve a abrir el archivo desde el principio y avanza
/// línea por línea hasta <see cref="ImportJob.UltimaFilaCheckpoint"/> en CADA disparo -- costo O(filas ya
/// procesadas) por ciclo, aceptable para el tamaño de archivo de referencia de este módulo (máximo 10 MB,
/// ver <c>IniciarImportacionCommandValidator</c>), pero no para archivos de millones de filas con miles de
/// ciclos -- pendiente explícito, ver <c>docs/guia-import-export.md</c>, sección "Límites de tamaño de
/// archivo".
/// </para>
/// </remarks>
public sealed class ImportBatchProcessorJob(
    ImportExportDbContext dbContext, IImportExportFileStore fileStore, IEnumerable<IImportRowHandler> rowHandlers,
    IOptions<ImportExportOptions> options)
    : IJob
{
    public Task Execute(IJobExecutionContext context) => ProcesarPendientesAsync(context.CancellationToken);

    /// <summary>Lógica real del job, separada de <see cref="Execute"/> para poder probarla sin construir un
    /// <see cref="IJobExecutionContext"/> real de Quartz -- mismo criterio que
    /// <c>IntegrationOutboundProcessorJob.ProcesarPendientesAsync</c>.</summary>
    public async Task ProcesarPendientesAsync(CancellationToken cancellationToken)
    {
        var pendientes = await dbContext.ImportJobs
            .IgnoreQueryFilters()
            .Where(j => j.Estado == ImportJobEstado.Pendiente || j.Estado == ImportJobEstado.EnProgreso)
            .ToListAsync(cancellationToken);

        if (pendientes.Count == 0)
        {
            return;
        }

        foreach (var job in pendientes)
        {
            try
            {
                await ProcesarUnChunkAsync(job, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Aislamiento por ítem a nivel de JOB completo -- ver el remarks de esta clase.
                job.MarcarFallido($"Error inesperado procesando el trabajo de importación: {ex.Message}", DateTime.UtcNow);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcesarUnChunkAsync(ImportJob job, CancellationToken cancellationToken)
    {
        var handler = rowHandlers.FirstOrDefault(h => h.TipoImportacion == job.TipoImportacion);
        if (handler is null)
        {
            job.MarcarFallido(
                $"No hay un manejador de importación (IImportRowHandler) registrado para el tipo '{job.TipoImportacion}'.",
                DateTime.UtcNow);
            return;
        }

        Stream stream;
        try
        {
            stream = await fileStore.OpenReadAsync(job.ArchivoBlobKey, cancellationToken);
        }
        catch (FileNotFoundException ex)
        {
            job.MarcarFallido($"El archivo original ya no está disponible: {ex.Message}", DateTime.UtcNow);
            return;
        }

        job.IniciarProcesamiento();

        await using (stream)
        using (var reader = new StreamReader(stream))
        {
            var headerLine = await reader.ReadLineAsync(cancellationToken);
            if (headerLine is null)
            {
                job.MarcarFallido("El archivo está vacío (no tiene encabezado).", DateTime.UtcNow);
                return;
            }

            var columnas = CsvLineParser.ParseLine(headerLine);

            // Salta las filas ya procesadas y confirmadas en ciclos anteriores -- ver el remarks de esta
            // clase para la limitación de performance de este enfoque.
            for (var i = 0; i < job.UltimaFilaCheckpoint; i++)
            {
                if (await reader.ReadLineAsync(cancellationToken) is null)
                {
                    break;
                }
            }

            var tamanoLote = Math.Max(1, options.Value.TamanoLoteFilas);
            var filasEnChunk = 0;
            var erroresEnChunk = 0;
            var numeroFila = job.UltimaFilaCheckpoint;

            string? linea;
            while (filasEnChunk < tamanoLote && (linea = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                numeroFila++;
                filasEnChunk++;

                if (string.IsNullOrWhiteSpace(linea))
                {
                    // Línea en blanco (típicamente al final del archivo por un salto de línea extra) --
                    // cuenta para el checkpoint (ContarFilasDeDatos ya la contó en FilasTotales) pero no es
                    // ni una fila válida ni un error a reportar.
                    continue;
                }

                try
                {
                    var valores = CsvLineParser.ParseLine(linea);
                    var fila = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (var c = 0; c < columnas.Length; c++)
                    {
                        fila[columnas[c]] = c < valores.Length ? valores[c] : string.Empty;
                    }

                    var columnasFaltantes = handler.ColumnasRequeridas
                        .Where(r => !fila.TryGetValue(r, out var valor) || string.IsNullOrWhiteSpace(valor))
                        .ToList();
                    if (columnasFaltantes.Count > 0)
                    {
                        RegistrarError(job, numeroFila, $"Faltan columnas requeridas: {string.Join(", ", columnasFaltantes)}.", linea);
                        erroresEnChunk++;
                        continue;
                    }

                    var resultado = await handler.ProcesarFilaAsync(job.TenantId, fila, cancellationToken);
                    if (resultado.IsFailure)
                    {
                        RegistrarError(job, numeroFila, resultado.Error.Description, linea);
                        erroresEnChunk++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Aislamiento por ítem a nivel de FILA -- ver el remarks de esta clase.
                    RegistrarError(job, numeroFila, $"Excepción no controlada procesando la fila: {ex.Message}", linea);
                    erroresEnChunk++;
                }
            }

            var quedanMasFilas = reader.Peek() != -1;

            job.RegistrarProgresoChunk(filasEnChunk, erroresEnChunk, numeroFila);

            if (!quedanMasFilas)
            {
                job.MarcarCompletado(DateTime.UtcNow);
            }
        }
    }

    /// <summary>TenantId asignado explícitamente (no vía TenantSaveChangesInterceptor, que no tiene ningún
    /// tenant "actual" que asumir en un job cross-tenant sin HttpContext) -- se toma del mismo tenant que
    /// el <see cref="ImportJob"/> procesado, leído con IgnoreQueryFilters más arriba. Mismo criterio que
    /// <c>IntegrationOutboundProcessorJob.AgregarLog</c>.</summary>
    private void RegistrarError(ImportJob job, int numeroFila, string mensajeError, string contenidoFilaCrudo)
    {
        dbContext.ImportJobErrors.Add(new ImportJobError(Guid.NewGuid(), job.Id, numeroFila, mensajeError, contenidoFilaCrudo)
        {
            TenantId = job.TenantId,
        });
    }
}
