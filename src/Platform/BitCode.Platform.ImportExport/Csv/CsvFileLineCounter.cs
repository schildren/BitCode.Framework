namespace BitCode.Framework.Platform.ImportExport.Csv;

/// <summary>
/// Cuenta las filas de DATOS (sin contar el encabezado) de un archivo CSV a partir de su contenido en
/// texto -- usado por <c>IniciarImportacionCommandHandler</c> para fijar <c>ImportJob.FilasTotales</c> en
/// el momento del alta (una única lectura completa en memoria, aceptable para el tamaño de archivo de
/// referencia de este módulo; ver <c>docs/guia-import-export.md</c>, sección "Límites de tamaño de
/// archivo", para el pendiente explícito de archivos muy grandes). La lógica de conteo replica
/// deliberadamente cómo <see cref="StreamReader.ReadLine"/> secuenciaría las mismas líneas en
/// <c>Procesamiento.ImportBatchProcessorJob</c>, para que el número mostrado como "total" en el progreso
/// coincida con la cantidad real de líneas que el job va a recorrer.
/// </summary>
internal static class CsvFileLineCounter
{
    public static int ContarFilasDeDatos(string contenidoTexto)
    {
        var lineas = contenidoTexto.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var cantidadLineas = lineas.Length;

        // StreamReader.ReadLine no emite una línea vacía adicional por el salto de línea final del
        // archivo -- se descuenta acá para que el conteo coincida.
        if (cantidadLineas > 0 && lineas[^1].Length == 0)
        {
            cantidadLineas--;
        }

        return Math.Max(0, cantidadLineas - 1);
    }
}
