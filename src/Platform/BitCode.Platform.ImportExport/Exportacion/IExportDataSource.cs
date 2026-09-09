namespace BitCode.Framework.Platform.ImportExport.Exportacion;

/// <summary>Una página de filas devuelta por <see cref="IExportDataSource.ObtenerPaginaAsync"/> -- cada
/// fila ya es una lista de valores en el mismo orden que <see cref="IExportDataSource.Columnas"/>.</summary>
/// <param name="Filas">Como máximo <c>tamanoLote</c> filas -- puede ser menor si la fuente no tiene tantas
/// filas restantes.</param>
/// <param name="HayMasFilas">Si es <see langword="false"/>, esta fue la última página -- el job marca el
/// <see cref="ExportJob"/> como completado tras procesarla.</param>
/// <param name="TotalFilasConocido">Total de filas de la fuente completa, si se conoce de antemano --
/// <see langword="null"/> si la fuente no puede calcularlo sin recorrerla entera (ver el <c>remarks</c> de
/// <see cref="ExportJob.FilasTotales"/>).</param>
public sealed record ExportPageResult(IReadOnlyList<IReadOnlyList<string>> Filas, bool HayMasFilas, int? TotalFilasConocido);

/// <summary>
/// Punto de extensión pluggable que un consumidor real implementa por CADA tipo de exportación que quiera
/// soportar (Fase 6, módulo 10) -- simétrico a <c>Importacion.IImportRowHandler</c>: este módulo no conoce
/// ningún modelo de negocio concreto, solo sabe pedir páginas sucesivas, escribirlas como CSV en chunks,
/// aislar errores por fila y persistir el progreso/checkpoint. Un host real la implementa y la registra en
/// su contenedor de DI (<c>services.AddScoped&lt;IExportDataSource, MiFuente&gt;()</c>) --
/// <c>Procesamiento.ExportBatchProcessorJob</c> resuelve <c>IEnumerable&lt;IExportDataSource&gt;</c> y
/// elige la que coincide con <see cref="TipoExportacion"/>.
/// </summary>
public interface IExportDataSource
{
    /// <summary>Código que un cliente HTTP usa en <c>IniciarExportacionCommand.TipoExportacion</c> para
    /// elegir esta implementación.</summary>
    string TipoExportacion { get; }

    /// <summary>Encabezado del CSV de resultado, en el mismo orden en que cada fila de
    /// <see cref="ExportPageResult.Filas"/> debe interpretarse.</summary>
    IReadOnlyList<string> Columnas { get; }

    /// <summary>
    /// Devuelve como máximo <paramref name="tamanoLote"/> filas a partir de la posición
    /// <paramref name="offset"/> (0-based, mismo valor que <see cref="ExportJob.UltimoOffsetExportado"/>).
    /// <paramref name="tenantId"/> es el tenant dueño del <see cref="ExportJob"/> -- mismo motivo que
    /// <c>Importacion.IImportRowHandler.ProcesarFilaAsync</c>: <c>ExportBatchProcessorJob</c> corre
    /// cross-tenant, así que esta implementación debe filtrar su propio modelo de negocio por este valor
    /// explícitamente. <paramref name="filtroJson"/> es el filtro opcional recibido en el alta del job,
    /// tal cual -- esta implementación decide cómo interpretarlo (o ignorarlo). Debe ser una fuente
    /// ESTABLE entre llamadas sucesivas con el mismo <paramref name="offset"/> (ver el <c>remarks</c> de
    /// <see cref="ExportJob"/>, sección "Reanudación": un offset reprocesado tras una caída debe devolver
    /// las mismas filas, en el mismo orden, o el archivo de resultado queda inconsistente).
    /// </summary>
    Task<ExportPageResult> ObtenerPaginaAsync(
        Guid tenantId, string? filtroJson, int offset, int tamanoLote, CancellationToken cancellationToken);
}
