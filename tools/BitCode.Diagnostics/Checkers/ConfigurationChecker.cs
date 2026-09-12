using System.Text.Json;
using BitCode.Framework.Tools.Diagnostics.Core;

namespace BitCode.Framework.Tools.Diagnostics.Checkers;

/// <summary>
/// Valida archivos de manifiesto, variables de entorno y archivos de configuración esenciales.
/// </summary>
public sealed class ConfigurationChecker : IDiagnosticChecker
{
    private readonly string _baseDirectory;

    public ConfigurationChecker(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? Directory.GetCurrentDirectory();
    }

    public DiagnosticCategory Category => DiagnosticCategory.Configuration;

    public Task<IReadOnlyList<DiagnosticItem>> RunChecksAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<DiagnosticItem>();

        items.Add(CheckManifestFile("docker-compose.yml", isRequired: true, "Archivo orquestador de dependencias locales (F8-11)."));
        items.Add(CheckManifestFile("scripts/dev-env.ps1", isRequired: true, "Script de administración PowerShell del entorno local."));
        items.Add(CheckManifestFile("scripts/dev-env.sh", isRequired: false, "Script de administración Bash del entorno local."));

        items.Add(CheckEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Server=localhost,1433;Database=BitCodeSample;User Id=sa;Password=Password123!;TrustServerCertificate=True;",
            "Cadena de conexión a SQL Server"));

        items.Add(CheckEnvironmentVariable(
            "Redis__Configuration",
            "localhost:6379",
            "Endpoint del broker de cache distribuido Redis"));

        items.Add(CheckEnvironmentVariable(
            "Kafka__BootstrapServers",
            "localhost:9092",
            "Dirección de brokers de Apache Kafka"));

        items.Add(CheckEnvironmentVariable(
            "OpenTelemetry__Endpoint",
            "http://localhost:4317",
            "Endpoint OTLP gRPC del OpenTelemetry Collector"));

        items.Add(CheckSampleApiSettings());

        return Task.FromResult<IReadOnlyList<DiagnosticItem>>(items);
    }

    private DiagnosticItem CheckManifestFile(string relativePath, bool isRequired, string description)
    {
        var fullPath = Path.Combine(_baseDirectory, relativePath);
        if (File.Exists(fullPath))
        {
            return DiagnosticItem.Success(
                DiagnosticCategory.Configuration,
                relativePath,
                $"{description} encontrado.",
                fullPath);
        }

        if (isRequired)
        {
            return DiagnosticItem.Error(
                DiagnosticCategory.Configuration,
                relativePath,
                $"No se encontró el archivo requerido: {relativePath}.",
                fullPath,
                $"Asegúrese de ejecutar el diagnóstico desde la raíz del repositorio o restaure {relativePath}.");
        }

        return DiagnosticItem.Warning(
            DiagnosticCategory.Configuration,
            relativePath,
            $"Archivo opcional no encontrado: {relativePath}.",
            fullPath);
    }

    private static DiagnosticItem CheckEnvironmentVariable(string name, string defaultValue, string description)
    {
        var val = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(val))
        {
            return DiagnosticItem.Success(
                DiagnosticCategory.Configuration,
                name,
                $"Variable {name} configurada explícitamente ({description}).",
                val);
        }

        return DiagnosticItem.Warning(
            DiagnosticCategory.Configuration,
            name,
            $"Variable {name} no configurada. Se usará fallback por defecto: {defaultValue}",
            $"Default: {defaultValue}",
            $"Para sobreescribir defina: $env:{name}=\"{defaultValue}\" o configure appsettings.Development.json.");
    }

    private DiagnosticItem CheckSampleApiSettings()
    {
        var sampleSettingsPath = Path.Combine(_baseDirectory, "samples", "Sample.Api", "appsettings.Development.json");
        if (!File.Exists(sampleSettingsPath))
        {
            sampleSettingsPath = Path.Combine(_baseDirectory, "samples", "Sample.Api", "appsettings.json");
        }

        if (!File.Exists(sampleSettingsPath))
        {
            return DiagnosticItem.Warning(
                DiagnosticCategory.Configuration,
                "Sample.Api appsettings",
                "No se encontró archivo appsettings en samples/Sample.Api.",
                sampleSettingsPath);
        }

        try
        {
            var content = File.ReadAllText(sampleSettingsPath);
            using var doc = JsonDocument.Parse(content);
            return DiagnosticItem.Success(
                DiagnosticCategory.Configuration,
                "Sample.Api appsettings",
                $"Configuración JSON válida en {Path.GetFileName(sampleSettingsPath)}.",
                sampleSettingsPath);
        }
        catch (Exception ex)
        {
            return DiagnosticItem.Error(
                DiagnosticCategory.Configuration,
                "Sample.Api appsettings",
                $"Error de sintaxis JSON en {sampleSettingsPath}: {ex.Message}",
                sampleSettingsPath,
                "Corrija la sintaxis del archivo JSON.");
        }
    }
}
