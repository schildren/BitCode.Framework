using BitCode.Framework.Tools.Diagnostics.Core;

namespace BitCode.Framework.Tools.Diagnostics.Formatters;

/// <summary>
/// Formatea el reporte de diagnóstico para salida de consola humana con colores y remediaciones claras.
/// </summary>
public static class ConsoleReportFormatter
{
    public static void Render(DiagnosticReport report)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("========================================================================");
        Console.WriteLine("      BitCode Diagnostics Doctor — Verificación de Entorno (F8-12)      ");
        Console.WriteLine("========================================================================");
        Console.ResetColor();
        Console.WriteLine($"Fecha de ejecución: {report.ExecutedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"Duración: {report.ElapsedMilliseconds} ms");
        Console.WriteLine();

        var categories = report.Items
            .GroupBy(i => i.Category)
            .OrderBy(g => (int)g.Key);

        foreach (var group in categories)
        {
            var header = group.Key switch
            {
                DiagnosticCategory.Tools => "🛠️  Herramientas y SDKs de Desarrollo",
                DiagnosticCategory.Configuration => "⚙️  Configuración y Manifiestos",
                DiagnosticCategory.Connectivity => "🌐 Conectividad de Infraestructura y Servicios",
                _ => group.Key.ToString()
            };

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"--- {header} ---");
            Console.ResetColor();

            foreach (var item in group)
            {
                RenderItem(item);
            }
            Console.WriteLine();
        }

        RenderSummary(report);
        RenderRemediations(report);
    }

    private static void RenderItem(DiagnosticItem item)
    {
        switch (item.Severity)
        {
            case DiagnosticSeverity.Success:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  [OK]   ");
                break;
            case DiagnosticSeverity.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("  [WARN] ");
                break;
            case DiagnosticSeverity.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [FAIL] ");
                break;
        }

        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{item.Name,-25}");
        Console.ResetColor();
        Console.WriteLine($" {item.Message}");

        if (!string.IsNullOrWhiteSpace(item.Details))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"         └─ Detalle: {item.Details}");
            Console.ResetColor();
        }
    }

    private static void RenderSummary(DiagnosticReport report)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("------------------------------------------------------------------------");
        Console.ResetColor();

        Console.Write("Resumen de comprobaciones: ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{report.Passed} superadas");
        Console.ResetColor();
        Console.Write(", ");

        if (report.Warnings > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"{report.Warnings} advertencias");
            Console.ResetColor();
        }
        else
        {
            Console.Write("0 advertencias");
        }

        Console.Write(", ");
        if (report.Failures > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"{report.Failures} errores");
            Console.ResetColor();
        }
        else
        {
            Console.Write("0 errores");
        }

        Console.WriteLine($" (Total: {report.TotalChecks})");
        Console.WriteLine();
    }

    private static void RenderRemediations(DiagnosticReport report)
    {
        var actionItems = report.Items
            .Where(i => i.Severity != DiagnosticSeverity.Success && !string.IsNullOrWhiteSpace(i.Remediation))
            .ToList();

        if (actionItems.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✨ ¡Todo el entorno se encuentra en estado óptimo! No se requieren acciones.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("💡 Acciones de Remediación Sugeridas:");
        Console.ResetColor();

        var index = 1;
        foreach (var item in actionItems)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"  {index++}. [{item.Name}]: ");
            Console.ResetColor();
            Console.WriteLine(item.Remediation);
        }

        Console.WriteLine();
    }
}
