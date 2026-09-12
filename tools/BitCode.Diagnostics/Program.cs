using System.Diagnostics;
using System.Text;
using BitCode.Framework.Tools.Diagnostics.Checkers;
using BitCode.Framework.Tools.Diagnostics.Core;
using BitCode.Framework.Tools.Diagnostics.Formatters;

namespace BitCode.Framework.Tools.Diagnostics;

/// <summary>
/// CLI para validación, diagnóstico accionable y health checking del entorno de desarrollo (Fase 8, F8-12).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Any(a => a is "-h" or "--help" or "help"))
        {
            PrintUsage();
            return 0;
        }

        var isJson = args.Any(a => a.Equals("--format=json", StringComparison.OrdinalIgnoreCase)
                                || a.Equals("json", StringComparison.OrdinalIgnoreCase)
                                || a.Equals("-o=json", StringComparison.OrdinalIgnoreCase));

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--format" or "-o" && args[i + 1].Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                isJson = true;
            }
        }

        var isStrict = args.Any(a => a.Equals("--strict", StringComparison.OrdinalIgnoreCase));

        var timeoutSeconds = 2;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--timeout" && int.TryParse(args[i + 1], out var parsedTimeout) && parsedTimeout > 0)
            {
                timeoutSeconds = parsedTimeout;
            }
        }

        var command = args.FirstOrDefault(a => !a.StartsWith('-'))?.ToLowerInvariant() ?? "doctor";

        var checkers = new List<IDiagnosticChecker>();

        switch (command)
        {
            case "tools":
                checkers.Add(new ToolsChecker());
                break;

            case "config":
                checkers.Add(new ConfigurationChecker());
                break;

            case "connectivity":
                checkers.Add(new ConnectivityChecker(TimeSpan.FromSeconds(timeoutSeconds)));
                break;

            case "doctor":
            case "check":
            case "all":
                checkers.Add(new ToolsChecker());
                checkers.Add(new ConfigurationChecker());
                checkers.Add(new ConnectivityChecker(TimeSpan.FromSeconds(timeoutSeconds)));
                break;

            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Comando desconocido: '{command}'. Use 'BitCode.Diagnostics --help' para ver las opciones disponibles.");
                Console.ResetColor();
                return 1;
        }

        var sw = Stopwatch.StartNew();
        var report = new DiagnosticReport();

        foreach (var checker in checkers)
        {
            try
            {
                var items = await checker.RunChecksAsync();
                report.AddRange(items);
            }
            catch (Exception ex)
            {
                report.Add(DiagnosticItem.Error(
                    checker.Category,
                    checker.GetType().Name,
                    $"Fallo interno al ejecutar verificador: {ex.Message}",
                    ex.ToString(),
                    "Verifique los permisos de ejecución del entorno."));
            }
        }

        sw.Stop();
        report.ElapsedMilliseconds = sw.ElapsedMilliseconds;

        if (isJson)
        {
            Console.WriteLine(JsonReportFormatter.Serialize(report));
        }
        else
        {
            ConsoleReportFormatter.Render(report);
        }

        if (report.HasErrors)
        {
            return 1;
        }

        if (isStrict && report.HasWarnings)
        {
            return 2;
        }

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("========================================================================");
        Console.WriteLine(" BitCode Diagnostics CLI — Diagnóstico Accionable de Entorno (F8-12)    ");
        Console.WriteLine("========================================================================");
        Console.WriteLine("Uso: dotnet run --project tools/BitCode.Diagnostics -- [comando] [opciones]\n");
        Console.WriteLine("Comandos:");
        Console.WriteLine("  doctor | check       Ejecuta diagnóstico completo (herramientas, config, conectividad).");
        Console.WriteLine("  tools                Valida presencia y versiones de .NET SDK, Node, Docker y Git.");
        Console.WriteLine("  config               Valida archivos base y variables de entorno del framework.");
        Console.WriteLine("  connectivity         Sondea conectividad TCP/HTTP a SQL Server, Redis, Kafka, OTel y Jaeger.");
        Console.WriteLine("\nOpciones:");
        Console.WriteLine("  --format json        Emite el reporte estructurado en JSON (útil para CI o scripts).");
        Console.WriteLine("  --strict             Devuelve código de salida distinto de 0 ante advertencias.");
        Console.WriteLine("  --timeout <segundos> Tiempo límite por chequeo de red (por defecto: 2s).");
        Console.WriteLine("  -h, --help           Muestra esta ayuda de uso.");
        Console.WriteLine();
    }
}
