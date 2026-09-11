using System.Text.Json;
using System.Text.Json.Serialization;
using BitCode.Framework.Tools.Diagnostics.Core;

namespace BitCode.Framework.Tools.Diagnostics.Formatters;

/// <summary>
/// Formateador de reporte para serialización en JSON estructurado.
/// </summary>
public static class JsonReportFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(DiagnosticReport report)
    {
        return JsonSerializer.Serialize(report, Options);
    }
}
