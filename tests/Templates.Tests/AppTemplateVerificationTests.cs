using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Testcontainers.MsSql;

namespace Templates.Tests;

/// <summary>
/// F8-01 (Plan Maestro, Fase 8): verifica que <c>dotnet new bitcode-app</c> no solo genera texto que
/// "se ve bien", sino una aplicación base REALMENTE ejecutable -- build de verdad contra los proyectos
/// reales del framework (mismo criterio que <see cref="TemplateVerificationTests"/>) y, además, un
/// proceso corriendo contra un SQL Server real (<see cref="MsSqlContainer"/>) que responde
/// /health/live, /health/ready y el CRUD de ejemplo (Elementos) end-to-end. Esta última parte es lo que
/// distingue esta plantilla de "proyecto" de las plantillas de "item" (domain-entity/feature-cqrs): esas
/// generan una pieza de código para pegar en un proyecto existente, esta genera un host completo que
/// debe poder arrancar.
/// </summary>
public sealed class AppTemplateVerificationTests : IAsyncLifetime
{
    private readonly string _repoRoot = FindRepoRoot();
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), $"bitcode-app-template-{Guid.NewGuid():N}");
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder().Build();

    public async Task InitializeAsync() => await _sqlServer.StartAsync();

    public async Task DisposeAsync()
    {
        await _sqlServer.DisposeAsync();

        // Reintenta el borrado: en Windows un file handle de una .dll recién cargada por el proceso que
        // acabamos de matar (WaitForExit ya esperó, pero un antivirus/indexador puede retener el archivo
        // brevemente) puede tardar unos milisegundos más en liberarse -- nunca falla el test por esto.
        for (var attempt = 1; Directory.Exists(_scratchDir); attempt++)
        {
            try
            {
                Directory.Delete(_scratchDir, recursive: true);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BitCode.Framework.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("No se encontró la raíz del repo (BitCode.Framework.slnx).");
    }

    private static (int ExitCode, string Output) RunDotnet(string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output);
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task AppTemplate_GeneratedApp_BuildsAndServesRealTrafficAgainstRealSqlServer()
    {
        var (installExitCode, installOutput) = RunDotnet(
            $"new install \"{Path.Combine(_repoRoot, "templates", "app")}\" --force", _repoRoot);
        installExitCode.Should().Be(0, installOutput);

        Directory.CreateDirectory(_scratchDir);
        var sharedSourceRoot = Path.Combine(_repoRoot, "src");

        var (generateExitCode, generateOutput) = RunDotnet(
            $"new bitcode-app -n Verification.Api -o \"{_scratchDir}\" --SharedSourceRoot \"{sharedSourceRoot.Replace('\\', '/')}\"",
            _repoRoot);
        generateExitCode.Should().Be(0, generateOutput);

        var (buildExitCode, buildOutput) = RunDotnet("build --nologo -c Debug", _scratchDir);
        buildExitCode.Should().Be(0, buildOutput);

        var dllPath = Directory.GetFiles(_scratchDir, "Verification.Api.dll", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains(Path.Combine("bin", "Debug")));
        dllPath.Should().NotBeNull("el build debe producir el ensamblado de salida en bin/Debug");

        var port = GetFreeTcpPort();
        var baseAddress = $"http://127.0.0.1:{port}";

        var startInfo = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            WorkingDirectory = _scratchDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = baseAddress;
        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.EnvironmentVariables["ConnectionStrings__Default"] = _sqlServer.GetConnectionString();

        var appOutput = new StringBuilder();
        using var appProcess = new Process { StartInfo = startInfo };
        appProcess.OutputDataReceived += (_, e) => { if (e.Data is not null) appOutput.AppendLine(e.Data); };
        appProcess.ErrorDataReceived += (_, e) => { if (e.Data is not null) appOutput.AppendLine(e.Data); };
        appProcess.Start();
        appProcess.BeginOutputReadLine();
        appProcess.BeginErrorReadLine();

        try
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };

            // La app arranca (AddModules/UseModules), crea el esquema (EnsureCreatedAsync) contra el SQL
            // Server real del contenedor y expone /health/live -- poll acotado, nunca un sleep fijo.
            var healthy = await WaitUntilHealthyAsync(httpClient, "/health/live", TimeSpan.FromSeconds(60));
            healthy.Should().BeTrue($"la app debería exponer /health/live saludable. Salida del proceso:\n{appOutput}");

            var ready = await httpClient.GetAsync("/health/ready");
            ready.StatusCode.Should().Be(HttpStatusCode.OK, $"Salida del proceso:\n{appOutput}");

            // CRUD de ejemplo (Elementos) end-to-end -- prueba que el pipeline CQRS + persistencia +
            // ToOkOrProblem/ToProblemDetails generado por el template funciona con datos reales, no solo
            // que el código compila.
            var createResponse = await httpClient.PostAsJsonAsync("/api/v1/elementos", new { Nombre = "Prueba" });
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created, $"Salida del proceso:\n{appOutput}");
            var createdId = await createResponse.Content.ReadFromJsonAsync<Guid>();

            var getResponse = await httpClient.GetAsync($"/api/v1/elementos/{createdId}");
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var listResponse = await httpClient.GetAsync("/api/v1/elementos?page=1&pageSize=20");
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var openApiResponse = await httpClient.GetAsync("/openapi/v1.json");
            openApiResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            if (!appProcess.HasExited)
            {
                appProcess.Kill(entireProcessTree: true);
            }

            // Espera a que el proceso (y con él, los file handles de sus .dll cargadas en el directorio
            // generado) termine de verdad antes de que DisposeAsync intente borrar _scratchDir -- Kill()
            // solo pide la terminación, no la garantiza sincrónicamente en Windows.
            appProcess.WaitForExit(5000);
        }
    }

    private static async Task<bool> WaitUntilHealthyAsync(HttpClient httpClient, string path, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var response = await httpClient.GetAsync(path, cts.Token);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return true;
                }
            }
            catch (Exception) when (!cts.IsCancellationRequested)
            {
                // La app todavía no levantó el listener -- reintenta hasta el timeout.
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return false;
    }
}
