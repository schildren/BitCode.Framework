using System.Text;

namespace BitCode.Framework.Platform.ImportExport.Csv;

/// <summary>
/// Parser CSV DELIBERADAMENTE simple (RFC 4180 acotado): separador coma, campos entre comillas dobles
/// soportando comas/comillas escapadas (<c>""</c>) DENTRO de una misma línea física -- NO soporta un campo
/// entre comillas que contenga un salto de línea embebido (un valor multilínea partiría el archivo en más
/// "filas" de las que en realidad tiene). Ver <c>docs/guia-import-export.md</c>, sección "Por qué CSV y qué
/// tan simple es este parser", para por qué esta tarea eligió CSV en vez de JSON y esta limitación honesta.
/// </summary>
internal static class CsvLineParser
{
    public static string[] ParseLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        result.Add(current.ToString());
        return [.. result];
    }

    /// <summary>Serializa un valor como un campo CSV -- lo envuelve entre comillas dobles (duplicando
    /// cualquier comilla interna) solo si contiene el separador, una comilla o un salto de línea; en caso
    /// contrario lo deja tal cual, mismo criterio "quote only when needed" de cualquier escritor CSV
    /// estándar.</summary>
    public static string WriteField(string? value)
    {
        value ??= string.Empty;
        var necesitaComillas = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!necesitaComillas)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
