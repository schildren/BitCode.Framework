using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F5-11 (Fase 5 — Disaster Resistance y multi-región): prueba de integración real, con DOS PROCESOS
/// DE SISTEMA OPERATIVO REALES (mismo estándar de evidencia que <c>docs/failover-automatizado-fase5.md</c>,
/// F5-10, y <c>docs/guia-quartz-ha.md</c> sección 6.1, F4-11), del comando <c>failback</c> de
/// <c>tools/RegionalFailoverHarness</c>.
///
/// A diferencia de un failover, un failback nunca puede ser "revertir sin más": si la región que vuelve
/// no está resincronizada, o si no hay una ventana explícita de no-doble-escritor durante la transición,
/// el resultado es split-brain (dos regiones creyéndose propietarias del mismo tenant al mismo tiempo, o
/// una región pisando datos más nuevos que los suyos). Esta prueba fuerza el escenario adversarial
/// exacto que pide el criterio de aceptación de F5-11 ("Evita split-brain"): durante la ventana de
/// failback, se dispara tráfico de escritura REAL (HTTP real sobre localhost) contra AMBAS regiones
/// SIMULTÁNEAMENTE, en un bucle de alta frecuencia, y se verifica -- con evidencia empírica, no solo
/// por diseño -- que en NINGÚN momento ambas regiones aceptan la escritura (200 OK) a la vez.
///
/// Ver <c>docs/failback-fase5.md</c> para el runbook completo y la relación con F5-10 (failover) y
/// F5-04/F5-05 (replicación real cuyo lag/RPO es lo que, en un despliegue real, alimentaría el
/// <c>--resync-url</c> que esta prueba simula con el comando <c>resync-set</c> del propio harness).
/// </summary>
public sealed class RegionFailbackNoSplitBrainIntegrationTests : IAsyncLifetime
{
    private readonly string _repoRoot = FindRepoRoot();
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), $"bitcode-f511-{Guid.NewGuid():N}");
    private string _harnessDll = string.Empty;
    private readonly List<Process> _spawnedProcesses = new();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workDir);

        var harnessProject = Path.Combine(_repoRoot, "tools", "RegionalFailoverHarness", "RegionalFailoverHarness.csproj");
        var (buildExitCode, buildOutput) = await RunDotnetAsync($"build \"{harnessProject}\" -c Debug --nologo", _repoRoot);
        buildExitCode.Should().Be(0, $"el harness de failover/failback (F5-10/F5-11) debe compilar antes de poder ejercitarse con procesos reales.\n{buildOutput}");

        _harnessDll = Path.Combine(_repoRoot, "tools", "RegionalFailoverHarness", "bin", "Debug", "net10.0", "RegionalFailoverHarness.dll");
        File.Exists(_harnessDll).Should().BeTrue($"se esperaba encontrar el binario compilado en '{_harnessDll}'.");
    }

    public Task DisposeAsync()
    {
        foreach (var process in _spawnedProcesses)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // El proceso ya pudo haber terminado por su cuenta (p. ej. el propio comando
                // "failback", que corre hasta completar y sale solo) -- no es un fallo de limpieza.
            }
            finally
            {
                process.Dispose();
            }
        }

        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch
        {
            // Best-effort: algún archivo pudo quedar bloqueado un instante por un proceso "node" que
            // recién está terminando de cerrarse -- no es motivo para fallar la prueba ya evaluada.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Failback_ConVentanaDeLock_NuncaAceptaEscriturasEnAmbasRegionesSimultaneamente()
    {
        var tenantId = Guid.NewGuid().ToString();
        var mapFile = Path.Combine(_workDir, "tenant-region-map.json");
        var auditLog = Path.Combine(_workDir, "audit-log.jsonl");
        var replicationStateFileA = Path.Combine(_workDir, "regionA-replication.json");
        var portA = GetFreeTcpPort();
        var portB = GetFreeTcpPort();

        // --- Arrange: init + dos nodos "region" como procesos de SO reales, regionA es la propietaria inicial ---
        var (initExitCode, initOutput) = await RunDotnetAsync($"\"{_harnessDll}\" init \"{mapFile}\" {tenantId} regionA", _workDir);
        initExitCode.Should().Be(0, initOutput);

        var nodeA = StartNode(mapFile, "regionA", portA, replicationStateFileA);
        var nodeB = StartNode(mapFile, "regionB", portB, replicationStateFile: null);

        await WaitForHealthyAsync(portA);
        await WaitForHealthyAsync(portB);

        (await GetWriteStatusAsync(portA, tenantId)).Should().Be(HttpStatusCode.OK, "regionA es la propietaria inicial del tenant.");
        (await GetWriteStatusAsync(portB, tenantId)).Should().Be(HttpStatusCode.MisdirectedRequest, "regionB nunca debe aceptar escrituras de un tenant que no le pertenece.");

        // --- Failover real: se mata regionA de verdad (mismo estándar de evidencia que F5-10) ---
        nodeA.Kill(entireProcessTree: true);
        nodeA.WaitForExit(5000);

        var (failoverExitCode, failoverOutput) = await RunDotnetAsync(
            $"\"{_harnessDll}\" failover \"{mapFile}\" \"{auditLog}\" {tenantId} regionB " +
            $"--health-url http://127.0.0.1:{portA}/health --threshold 2 --interval-seconds 1 --confirm",
            _workDir);
        failoverExitCode.Should().Be(0, failoverOutput);

        await WaitUntilAsync(async () => await GetWriteStatusAsync(portB, tenantId) == HttpStatusCode.OK,
            "regionB debe pasar a aceptar escrituras tras el failover (hot-reload del mapa, F4-12).");

        // --- La región original vuelve a estar sana, pero SIN evidencia de resincronización todavía ---
        var nodeARecovered = StartNode(mapFile, "regionA", portA, replicationStateFileA);
        await WaitForHealthyAsync(portA);

        (await GetWriteStatusAsync(portA, tenantId)).Should().Be(
            HttpStatusCode.MisdirectedRequest,
            "una región recién recuperada, aunque responda a health-check, NO debe aceptar escrituras hasta un failback explícito -- lo contrario sería split-brain inmediato.");

        // --- Failback SIN resincronización confirmada: debe abortar, nunca revertir "a ciegas" ---
        var (prematureExitCode, prematureOutput) = await RunDotnetAsync(
            $"\"{_harnessDll}\" failback \"{mapFile}\" \"{auditLog}\" {tenantId} regionA " +
            $"--resync-url http://127.0.0.1:{portA}/replication-status --resync-threshold 2 --resync-interval-seconds 1 --confirm",
            _workDir);

        prematureExitCode.Should().Be(2, "el failback debe abortar (fallo cerrado) cuando la región candidata no reporta estar resincronizada.\n" + prematureOutput);
        prematureOutput.Should().Contain("NO demostró estar resincronizada", "el motivo del aborto debe ser explícito en la salida del comando.");
        (await GetWriteStatusAsync(portB, tenantId)).Should().Be(HttpStatusCode.OK, "sin resincronización confirmada, regionB debe seguir siendo la única propietaria.");

        // --- Se confirma la resincronización (en un despliegue real: RPO real de F5-04/F5-05) ---
        var (resyncExitCode, resyncOutput) = await RunDotnetAsync(
            $"\"{_harnessDll}\" resync-set \"{replicationStateFileA}\" {tenantId} 0",
            _workDir);
        resyncExitCode.Should().Be(0, resyncOutput);

        // El hot-reload de nodeA (AddJsonFile(reloadOnChange: true), F4-12) del archivo de estado de
        // replicación no es instantáneo -- se espera a que el propio endpoint de regionA confirme
        // "caughtUp" antes de disparar el failback, igual que un operador real esperaría la métrica
        // de RPO real (F5-04/F5-05) antes de decidir revertir, en vez de asumir que "ya se escribió el
        // archivo" significa "ya se leyó".
        await WaitUntilAsync(async () =>
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            try
            {
                var response = await httpClient.GetAsync($"http://127.0.0.1:{portA}/replication-status/{tenantId}");
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return false;
                }

                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject;
                return json?["caughtUp"]?.GetValue<bool>() ?? false;
            }
            catch
            {
                return false;
            }
        }, "regionA debe reflejar (vía hot-reload) el lag de replicación puesto a 0 por 'resync-set' antes de intentar el failback real.");

        // --- Escenario adversarial: se dispara el failback (con ventana de lock) en background y,
        //     MIENTRAS corre, se golpean ambas regiones con escrituras reales a alta frecuencia. ---
        const int lockHoldSeconds = 4;
        var failbackTask = RunDotnetAsync(
            $"\"{_harnessDll}\" failback \"{mapFile}\" \"{auditLog}\" {tenantId} regionA " +
            $"--resync-url http://127.0.0.1:{portA}/replication-status --resync-threshold 2 --resync-interval-seconds 1 " +
            $"--lock-hold-seconds {lockHoldSeconds} --confirm",
            _workDir);

        var observations = new List<(HttpStatusCode A, HttpStatusCode B)>();
        var stopwatch = Stopwatch.StartNew();

        // Se observa durante bastante más que resync-check (~2s) + lock-hold (4s) para capturar las
        // tres fases: propietaria única (regionB), ventana de lock (ambas bloqueadas), y propietaria
        // única de nuevo (regionA) -- sin perder ninguna transición por baja frecuencia de muestreo.
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(12) && !failbackTask.IsCompleted)
        {
            var statusA = await GetWriteStatusAsync(portA, tenantId);
            var statusB = await GetWriteStatusAsync(portB, tenantId);
            observations.Add((statusA, statusB));
            await Task.Delay(150);
        }

        var (failbackExitCode, failbackOutput) = await failbackTask;
        failbackExitCode.Should().Be(0, failbackOutput);

        // --- Aserción central del criterio de aceptación ("Evita split-brain"): en NINGUNA observación
        //     ambas regiones devolvieron 200 OK al mismo tiempo. ---
        observations.Should().NotBeEmpty("la ventana de observación debe haber capturado tráfico real durante el failback.");
        observations.Should().NotContain(
            o => o.A == HttpStatusCode.OK && o.B == HttpStatusCode.OK,
            "split-brain = ambas regiones aceptando escrituras del mismo tenant al mismo tiempo; el lock de F5-11 debe impedirlo siempre.");

        // --- Se observó de verdad la ventana de no-doble-escritor (ambas bloqueadas), no solo el
        //     antes/después -- si esto no se observa, el lock podría no estarse aplicando realmente. ---
        observations.Should().Contain(
            o => o.A == HttpStatusCode.ServiceUnavailable && o.B == HttpStatusCode.ServiceUnavailable,
            "debe existir al menos una observación real donde AMBAS regiones rechazan la escritura por el lock explícito de no-doble-escritor.");

        // --- Estado final: exactamente una propietaria (regionA), sin ambigüedad. Se espera (con
        //     tolerancia) a que el hot-reload final de ambos nodos propague el desbloqueo + la nueva
        //     asignación -- el propio comando "failback" ya salió (exit code 0) antes de que el
        //     archivo llegue a ser releído por los procesos "node", igual que ocurre con cualquier
        //     ConfigMap real (F4-12). ---
        await WaitUntilAsync(async () => await GetWriteStatusAsync(portA, tenantId) == HttpStatusCode.OK,
            "regionA debe pasar a aceptar escrituras tras el failback (hot-reload del mapa + liberación del lock).");
        (await GetWriteStatusAsync(portB, tenantId)).Should().Be(HttpStatusCode.MisdirectedRequest, "tras el failback, regionB ya no debe aceptar escrituras del tenant.");

        var auditContent = await File.ReadAllTextAsync(auditLog);
        auditContent.Should().Contain("\"result\":\"aborted-not-resynced\"", "el intento prematuro de failback debe quedar auditado, igual que uno exitoso.");
        auditContent.Should().Contain("\"result\":\"lock-applied\"", "la activación del lock debe quedar auditada de forma independiente de la promoción.");
        auditContent.Should().Contain("\"result\":\"promoted\"", "la promoción final tras la resincronización confirmada debe quedar auditada.");
    }

    private Process StartNode(string mapFile, string regionId, int port, string? replicationStateFile)
    {
        var replicationArg = replicationStateFile is null ? string.Empty : $" \"{replicationStateFile}\"";
        var startInfo = new ProcessStartInfo(
            "dotnet",
            $"\"{_harnessDll}\" node \"{mapFile}\" {regionId} {port}{replicationArg}")
        {
            WorkingDirectory = _workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"No se pudo arrancar el nodo '{regionId}'.");
        _spawnedProcesses.Add(process);
        return process;
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdoutTask + await stderrTask;

        return (process.ExitCode, output);
    }

    private static async Task WaitForHealthyAsync(int port)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        await WaitUntilAsync(async () =>
        {
            try
            {
                var response = await httpClient.GetAsync($"http://127.0.0.1:{port}/health");
                return response.StatusCode == HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }, $"el nodo en el puerto {port} debe responder /health.");
    }

    private static async Task<HttpStatusCode> GetWriteStatusAsync(int port, string tenantId)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            var response = await httpClient.GetAsync($"http://127.0.0.1:{port}/write/{tenantId}");
            return response.StatusCode;
        }
        catch
        {
            return HttpStatusCode.ServiceUnavailable;
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string because, int timeoutSeconds = 15)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Condición no cumplida a tiempo: {because}");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
}
