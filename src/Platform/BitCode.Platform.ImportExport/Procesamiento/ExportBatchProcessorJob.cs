using System.Text;
using BitCode.Framework.Platform.ImportExport.Almacenamiento;
using BitCode.Framework.Platform.ImportExport.Csv;
using BitCode.Framework.Platform.ImportExport.Exportacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace BitCode.Framework.Platform.ImportExport.Procesamiento;

/// <summary>
/// Procesa, en cada disparo, UN chunk de cada <see cref="ExportJob"/> pendiente -- simétrico a
/// <see cref="ImportBatchProcessorJob"/> (ver su <c>remarks</c> para el mismo detalle de registro con
/// Quartz HA, aislamiento por ítem en dos niveles e <c>IgnoreQueryFilters()</c>/<c>SaveChangesAsync</c>
/// explícito como excepción legítima a la regla dura 1/5).
/// </summary>
/// <remarks>
/// <b>Reanudación -- ver el <c>remarks</c> de <see cref="ExportJob"/> para la limitación honesta
/// específica de este job</b>: a diferencia de la importación (que es puramente idempotente por
/// reproceso de filas de LECTURA), este job hace una escritura de ARCHIVO (<c>AppendAsync</c>) seguida de
/// una escritura de BASE DE DATOS (<c>SaveChangesAsync</c> del checkpoint) -- una caída entre ambas deja el
/// archivo con un chunk ya escrito pero el checkpoint sin avanzar, y el próximo ciclo reescribe ese mismo
/// chunk, duplicando filas en el archivo de resultado.
/// <para>
/// <b>La ventana real es más amplia de lo que sugiere "entre AppendAsync y SaveChangesAsync de ESE
/// job":</b> <c>SaveChangesAsync</c> (línea 56) se llama UNA sola vez, después del <c>foreach</c> que
/// procesa TODOS los <see cref="Exportacion.ExportJob"/> pendientes del ciclo -- no inmediatamente
/// después del <c>AppendAsync</c> de cada job individual. Si hay N jobs pendientes en el mismo disparo,
/// el checkpoint de un job procesado temprano en el <c>foreach</c> queda sin confirmar en base de datos
/// durante TODO el tiempo que toma procesar los N-1 jobs restantes (incluyendo sus propias llamadas a
/// <see cref="Exportacion.IExportDataSource"/>/<c>AppendAsync</c>, que pueden tardar) -- no solo el
/// instante entre su propio <c>AppendAsync</c> y el `SaveChangesAsync` final. Una caída del proceso en
/// cualquier punto de ese intervalo más largo produce el mismo duplicado ya documentado, con una
/// probabilidad de ocurrencia mayor a la que "la ventana entre dos líneas consecutivas" sugeriría.
/// Hallazgo de auditoría de arquitectura (2026-09-09), corregido acá solo como precisión de
/// documentación -- el riesgo de fondo (duplicado tras una caída) ya estaba documentado honestamente, no
/// se corrige el código en este corte (requeriría mover `SaveChangesAsync` dentro del `try` de cada job,
/// perdiendo el batching de performance entre jobs, o un checkpoint de más grano fino).
/// </para>
/// </remarks>
public sealed class ExportBatchProcessorJob(
    ImportExportDbContext dbContext, IImportExportFileStore fileStore, IEnumerable<IExportDataSource> dataSources,
    IOptions<ImportExportOptions> options)
    : IJob
{
    public Task Execute(IJobExecutionContext context) => ProcesarPendientesAsync(context.CancellationToken);

    public async Task ProcesarPendientesAsync(CancellationToken cancellationToken)
    {
        var pendientes = await dbContext.ExportJobs
            .IgnoreQueryFilters()
            .Where(j => j.Estado == ExportJobEstado.Pendiente || j.Estado == ExportJobEstado.EnProgreso)
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
                job.MarcarFallido($"Error inesperado procesando el trabajo de exportación: {ex.Message}", DateTime.UtcNow);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcesarUnChunkAsync(ExportJob job, CancellationToken cancellationToken)
    {
        var dataSource = dataSources.FirstOrDefault(d => d.TipoExportacion == job.TipoExportacion);
        if (dataSource is null)
        {
            job.MarcarFallido(
                $"No hay una fuente de exportación (IExportDataSource) registrada para el tipo '{job.TipoExportacion}'.",
                DateTime.UtcNow);
            return;
        }

        var esPrimerChunk = job.Estado == ExportJobEstado.Pendiente;
        if (esPrimerChunk)
        {
            var encabezado = string.Join(',', dataSource.Columnas.Select(CsvLineParser.WriteField)) + "\r\n";
            await fileStore.UploadAsync(job.ArchivoResultadoBlobKey, new MemoryStream(Encoding.UTF8.GetBytes(encabezado)), cancellationToken);
            job.IniciarProcesamiento();
        }

        var tamanoLote = Math.Max(1, options.Value.TamanoLoteFilas);

        // Fallo al pedir la página completa -- job-level (no hay ninguna fila individual sobre la que
        // aislar el error, ver el remarks de ExportJob).
        ExportPageResult pagina;
        try
        {
            pagina = await dataSource.ObtenerPaginaAsync(
                job.TenantId, job.FiltroJson, job.UltimoOffsetExportado, tamanoLote, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.MarcarFallido($"La fuente de exportación falló al pedir la página de datos: {ex.Message}", DateTime.UtcNow);
            return;
        }

        if (pagina.TotalFilasConocido is not null)
        {
            job.ActualizarTotalConocido(pagina.TotalFilasConocido.Value);
        }

        var textoDelChunk = new StringBuilder();
        var erroresEnChunk = 0;
        var filasExportadasEnChunk = 0;

        for (var i = 0; i < pagina.Filas.Count; i++)
        {
            var numeroFila = job.UltimoOffsetExportado + i;
            try
            {
                var linea = string.Join(',', pagina.Filas[i].Select(CsvLineParser.WriteField));
                textoDelChunk.Append(linea).Append("\r\n");
                filasExportadasEnChunk++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Aislamiento por ítem -- ver el remarks de ImportBatchProcessorJob, mismo criterio
                // simétrico para exportación.
                RegistrarError(job, numeroFila, $"Excepción no controlada serializando la fila a CSV: {ex.Message}");
                erroresEnChunk++;
            }
        }

        if (textoDelChunk.Length > 0)
        {
            await fileStore.AppendAsync(job.ArchivoResultadoBlobKey, textoDelChunk.ToString(), cancellationToken);
        }

        var nuevoOffset = job.UltimoOffsetExportado + pagina.Filas.Count;
        job.RegistrarProgresoChunk(filasExportadasEnChunk, erroresEnChunk, nuevoOffset);

        if (!pagina.HayMasFilas)
        {
            job.MarcarCompletado(DateTime.UtcNow);
        }
    }

    private void RegistrarError(ExportJob job, int numeroFila, string mensajeError)
    {
        dbContext.ExportJobErrors.Add(new ExportJobError(Guid.NewGuid(), job.Id, numeroFila, mensajeError)
        {
            TenantId = job.TenantId,
        });
    }
}
