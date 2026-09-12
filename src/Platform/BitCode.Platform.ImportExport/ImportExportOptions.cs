namespace BitCode.Framework.Platform.ImportExport;

/// <summary>
/// Configuración del motor de lotes de este módulo (Fase 6, módulo 10: "lotes" del Plan Maestro). Un
/// consumidor real la enlaza desde su propia configuración, mismo patrón que <c>IntegrationHubOptions</c>.
/// </summary>
public sealed class ImportExportOptions
{
    /// <summary>Cantidad de filas de datos que <c>ImportBatchProcessorJob</c>/<c>ExportBatchProcessorJob</c>
    /// procesan por CADA <c>ImportJob</c>/<c>ExportJob</c> pendiente en un mismo disparo del job -- el
    /// resto queda para el próximo disparo, con el progreso ya persistido (checkpoint). Valor deliberadamente
    /// chico por defecto para que las pruebas de integración puedan observar varios ciclos de progreso sin
    /// archivos de miles de filas; un consumidor productivo lo ajusta según el tamaño típico de sus
    /// archivos y la frecuencia del trigger de Quartz.</summary>
    public int TamanoLoteFilas { get; set; } = 200;
}
