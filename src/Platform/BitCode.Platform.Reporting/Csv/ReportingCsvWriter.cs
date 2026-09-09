using System.Text;

namespace BitCode.Framework.Platform.Reporting.Csv;

/// <summary>
/// Escritor CSV mínimo, propio de este módulo (Fase 6, módulo 11: "exportación" del Plan Maestro) --
/// implementación deliberadamente independiente de <c>BitCode.Platform.ImportExport.Csv.CsvLineParser</c>
/// (Fase 6, módulo 10): mismo criterio de aislamiento entre bounded contexts que ya aplican
/// consistentemente el resto de los módulos de Fase 6 (Documents no reutiliza Notifications, Import and
/// Export no reutiliza Documents, etc. — ver <c>docs/guia-reporting.md</c>, "Decisiones de diseño").
/// Reutilizar el escritor de otro módulo acoplaría este bounded context a la implementación interna de
/// otro, exactamente lo que la sección 3.2 del Plan Maestro prohíbe ("no compartir el DbContext ni el
/// almacenamiento de otro módulo").
/// </summary>
internal static class ReportingCsvWriter
{
    /// <summary>Escribe una fila CSV completa (con <c>\r\n</c> final) escapando cada campo según RFC 4180:
    /// un campo que contenga coma, comilla doble o salto de línea se envuelve entre comillas dobles, con
    /// cada comilla doble interna duplicada.</summary>
    public static string WriteLine(IEnumerable<string?> campos)
    {
        var builder = new StringBuilder();
        var primero = true;

        foreach (var campo in campos)
        {
            if (!primero)
            {
                builder.Append(',');
            }

            primero = false;
            builder.Append(EscapeField(campo));
        }

        builder.Append("\r\n");
        return builder.ToString();
    }

    private static string EscapeField(string? campo)
    {
        if (string.IsNullOrEmpty(campo))
        {
            return string.Empty;
        }

        var necesitaComillas = campo.Contains(',') || campo.Contains('"') || campo.Contains('\n') || campo.Contains('\r');
        if (!necesitaComillas)
        {
            return campo;
        }

        return $"\"{campo.Replace("\"", "\"\"")}\"";
    }
}
