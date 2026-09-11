using BitCode.Framework.Tools.Diagnostics.Core;
using BitCode.Framework.Tools.Diagnostics.Utils;

namespace BitCode.Framework.Tools.Diagnostics.Checkers;

/// <summary>
/// Valida la presencia y versiones mínimas de las herramientas y SDKs de desarrollo.
/// </summary>
public sealed class ToolsChecker : IDiagnosticChecker
{
    public DiagnosticCategory Category => DiagnosticCategory.Tools;

    public async Task<IReadOnlyList<DiagnosticItem>> RunChecksAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<DiagnosticItem>();

        items.Add(await CheckDotNetSdkAsync(cancellationToken));
        items.Add(await CheckNodeJsAsync(cancellationToken));
        items.Add(await CheckGitAsync(cancellationToken));
        items.Add(await CheckDockerCliAsync(cancellationToken));
        items.Add(await CheckDockerComposeAsync(cancellationToken));
        items.Add(await CheckDockerDaemonAsync(cancellationToken));

        return items;
    }

    private static async Task<DiagnosticItem> CheckDotNetSdkAsync(CancellationToken cancellationToken)
    {
        var res = await ProcessRunner.RunAsync("dotnet", "--version", TimeSpan.FromSeconds(3), cancellationToken);
        if (res.ExitCode != 0 || string.IsNullOrWhiteSpace(res.StandardOutput))
        {
            return DiagnosticItem.Error(
                DiagnosticCategory.Tools,
                ".NET SDK",
                ".NET SDK no encontrado en el PATH.",
                res.StandardError,
                "Instale .NET SDK 10.x desde https://dot.net o verifique la variable PATH.");
        }

        var versionStr = res.StandardOutput.Split('-')[0];
        if (Version.TryParse(versionStr, out var version))
        {
            if (version.Major < 10)
            {
                return DiagnosticItem.Warning(
                    DiagnosticCategory.Tools,
                    ".NET SDK",
                    $"Versión de .NET SDK ({res.StandardOutput}) es inferior a .NET 10.",
                    $"Detectado: {res.StandardOutput}",
                    "El framework requiere .NET 10 para todas las capacidades y compiladores C# 13/14.");
            }

            return DiagnosticItem.Success(
                DiagnosticCategory.Tools,
                ".NET SDK",
                $".NET SDK instalado y compatible ({res.StandardOutput}).",
                $"Versión: {res.StandardOutput}");
        }

        return DiagnosticItem.Success(
            DiagnosticCategory.Tools,
            ".NET SDK",
            $".NET SDK detectado: {res.StandardOutput}.",
            $"Versión: {res.StandardOutput}");
    }

    private static async Task<DiagnosticItem> CheckNodeJsAsync(CancellationToken cancellationToken)
    {
        var res = await ProcessRunner.RunAsync("node", "--version", TimeSpan.FromSeconds(3), cancellationToken);
        if (res.ExitCode != 0 || string.IsNullOrWhiteSpace(res.StandardOutput))
        {
            return DiagnosticItem.Warning(
                DiagnosticCategory.Tools,
                "Node.js",
                "Node.js no encontrado en el PATH. Es requerido para compilar el frontend Angular y portal técnico.",
                res.StandardError,
                "Instale Node.js v20+ LTS desde https://nodejs.org/.");
        }

        var rawVersion = res.StandardOutput.TrimStart('v');
        if (Version.TryParse(rawVersion, out var version) && version.Major < 18)
        {
            return DiagnosticItem.Warning(
                DiagnosticCategory.Tools,
                "Node.js",
                $"Versión de Node.js ({res.StandardOutput}) es inferior a v18.",
                $"Detectado: {res.StandardOutput}",
                "Actualice Node.js a la versión LTS recomendada (v20 o superior).");
        }

        return DiagnosticItem.Success(
            DiagnosticCategory.Tools,
            "Node.js",
            $"Node.js instalado y compatible ({res.StandardOutput}).",
            $"Versión: {res.StandardOutput}");
    }

    private static async Task<DiagnosticItem> CheckGitAsync(CancellationToken cancellationToken)
    {
        var res = await ProcessRunner.RunAsync("git", "--version", TimeSpan.FromSeconds(3), cancellationToken);
        if (res.ExitCode != 0 || string.IsNullOrWhiteSpace(res.StandardOutput))
        {
            return DiagnosticItem.Warning(
                DiagnosticCategory.Tools,
                "Git",
                "Git no encontrado en el PATH.",
                res.StandardError,
                "Instale Git desde https://git-scm.com/.");
        }

        return DiagnosticItem.Success(
            DiagnosticCategory.Tools,
            "Git",
            $"Git disponible ({res.StandardOutput}).",
            $"Versión: {res.StandardOutput}");
    }

    private static async Task<DiagnosticItem> CheckDockerCliAsync(CancellationToken cancellationToken)
    {
        var res = await ProcessRunner.RunAsync("docker", "--version", TimeSpan.FromSeconds(3), cancellationToken);
        if (res.ExitCode != 0 || string.IsNullOrWhiteSpace(res.StandardOutput))
        {
            return DiagnosticItem.Warning(
                DiagnosticCategory.Tools,
                "Docker CLI",
                "Docker CLI no encontrado en el PATH.",
                res.StandardError,
                "Instale Docker Desktop o Docker Engine para ejecutar las dependencias locales.");
        }

        return DiagnosticItem.Success(
            DiagnosticCategory.Tools,
            "Docker CLI",
            $"Docker CLI disponible ({res.StandardOutput}).",
            $"Versión: {res.StandardOutput}");
    }

    private static async Task<DiagnosticItem> CheckDockerComposeAsync(CancellationToken cancellationToken)
    {
        var res = await ProcessRunner.RunAsync("docker", "compose version", TimeSpan.FromSeconds(3), cancellationToken);
        if (res.ExitCode != 0 || string.IsNullOrWhiteSpace(res.StandardOutput))
        {
            return DiagnosticItem.Warning(
                DiagnosticCategory.Tools,
                "Docker Compose",
                "Docker Compose v2 no disponible.",
                res.StandardError,
                "Asegúrese de habilitar Docker Compose v2 en la configuración de Docker Desktop.");
        }

        return DiagnosticItem.Success(
            DiagnosticCategory.Tools,
            "Docker Compose",
            $"Docker Compose v2 disponible ({res.StandardOutput}).",
            $"Versión: {res.StandardOutput}");
    }

    private static async Task<DiagnosticItem> CheckDockerDaemonAsync(CancellationToken cancellationToken)
    {
        var res = await ProcessRunner.RunAsync("docker", "info", TimeSpan.FromSeconds(3), cancellationToken);
        if (res.ExitCode != 0)
        {
            return DiagnosticItem.Warning(
                DiagnosticCategory.Tools,
                "Docker Daemon",
                "El demonio de Docker no responde o no está en ejecución.",
                res.StandardError,
                "Inicie Docker Desktop o el servicio dockerd antes de levantar el entorno local.");
        }

        return DiagnosticItem.Success(
            DiagnosticCategory.Tools,
            "Docker Daemon",
            "Demonio de Docker en ejecución y respondiendo.",
            "Docker Engine activo");
    }
}
