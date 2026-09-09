using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Tools.RegionalFailoverHarness;

/// <summary>
/// Herramienta de diagnóstico/operación manual, NO parte de la suite automatizada (mismo criterio que
/// <c>tools/QuartzHaRecoveryHarness</c>, F4-11), creada para F5-10 (Fase 5 — Disaster Recovery y
/// multi-región): demuestra, con DOS PROCESOS DE SISTEMA OPERATIVO REALES (no dos instancias del mismo
/// proceso .NET), el workflow de failover automatizado de la asignación tenant → región propietaria de
/// escritura que define <see cref="ITenantRegionMapStore"/> (F5-02) y que consume
/// <c>RegionalOwnershipRoutingMiddleware</c> (F5-03, BitCode.Gateway).
/// </summary>
/// <remarks>
/// Ver <c>docs/failover-automatizado-fase5.md</c> para el procedimiento completo y la evidencia real
/// de ejecución repetida (failover → failback → failover) obtenida con esta herramienta.
/// <code>
///   RegionalFailoverHarness.exe init     &lt;mapFile&gt; &lt;tenantId&gt; &lt;ownerRegion&gt;
///   RegionalFailoverHarness.exe node     &lt;mapFile&gt; &lt;regionId&gt; &lt;port&gt;
///   RegionalFailoverHarness.exe failover &lt;mapFile&gt; &lt;auditLog&gt; &lt;tenantId&gt; &lt;candidateRegion&gt;
///                                        [--health-url &lt;url&gt;] [--threshold &lt;n&gt;]
///                                        [--interval-seconds &lt;s&gt;] [--confirm]
///   RegionalFailoverHarness.exe status   &lt;mapFile&gt; &lt;auditLog&gt;
/// </code>
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            PrintUsage();
            return 1;
        }

        switch (args[0])
        {
            case "init" when args.Length >= 4:
                InitMap(args[1], args[2], args[3]);
                return 0;

            case "node" when args.Length >= 4:
                await RunNodeAsync(args[1], args[2], int.Parse(args[3]));
                return 0;

            case "failover" when args.Length >= 4:
                return await RunFailoverAsync(args[1], args[2], args[3], args[4], args[5..]);

            case "status" when args.Length >= 3:
                PrintStatus(args[1], args[2]);
                return 0;

            default:
                PrintUsage();
                return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Uso:");
        Console.WriteLine("  RegionalFailoverHarness init     <mapFile> <tenantId> <ownerRegion>");
        Console.WriteLine("  RegionalFailoverHarness node     <mapFile> <regionId> <port>");
        Console.WriteLine("  RegionalFailoverHarness failover <mapFile> <auditLog> <tenantId> <candidateRegion>");
        Console.WriteLine("                                   [--health-url <url>] [--threshold <n>]");
        Console.WriteLine("                                   [--interval-seconds <s>] [--confirm]");
        Console.WriteLine("  RegionalFailoverHarness status   <mapFile> <auditLog>");
    }

    // ---------------------------------------------------------------------------------------------
    // init: crea/reinicializa el mapa tenant -> región propietaria (fuente de verdad compartida por
    // todos los nodos "region" que se levanten apuntando al mismo archivo).
    // ---------------------------------------------------------------------------------------------

    private static void InitMap(string mapFile, string tenantId, string ownerRegion)
    {
        var doc = new JsonObject
        {
            ["TenantRegionMap"] = new JsonObject
            {
                [tenantId] = ownerRegion,
            },
        };

        File.WriteAllText(mapFile, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"init: '{mapFile}' creado. Tenant '{tenantId}' -> región propietaria '{ownerRegion}'.");
    }

    // ---------------------------------------------------------------------------------------------
    // node: un proceso de SO independiente que representa la instancia del Gateway/backend de UNA
    // región. Lee el mismo mapFile que las demás instancias vía AddJsonFile(reloadOnChange: true) --
    // el mismo mecanismo de hot-reload ya documentado en docs/politica-configuracion-y-feature-flags.md
    // sección 2.4 (F4-12, ConfigMap montado como volumen) -- así que un failover que reescribe el
    // archivo se refleja SIN reiniciar este proceso, igual que ocurriría con un ConfigMap real.
    // ---------------------------------------------------------------------------------------------

    private static async Task RunNodeAsync(string mapFile, string regionId, int port)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss.fff ";
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        // optional: true -- el archivo puede no existir todavía al arrancar (mismo criterio que F4-12
        // para el volumen de ConfigMap en desarrollo local sin volumen montado).
        builder.Configuration.AddJsonFile(mapFile, optional: true, reloadOnChange: true);

        var app = builder.Build();
        var currentRegion = new RegionId(regionId);
        var pid = Environment.ProcessId;

        app.MapGet("/health", () => Results.Ok(new { region = currentRegion.Value, pid, status = "up" }));

        app.MapGet("/write/{tenantId}", (string tenantId, HttpContext context) =>
        {
            var ownerRegionValue = app.Configuration[$"TenantRegionMap:{tenantId}"];
            var ownerRegion = string.IsNullOrWhiteSpace(ownerRegionValue) ? RegionId.Primary : new RegionId(ownerRegionValue);

            if (ownerRegion != currentRegion)
            {
                context.Response.Headers[RegionalFailoverConstants.OwnerRegionHeaderName] = ownerRegion.Value;
                app.Logger.LogWarning(
                    "[{Region}] Write RECHAZADO para tenant {TenantId}: propietaria es '{Owner}', esta instancia es '{Current}'.",
                    currentRegion.Value,
                    tenantId,
                    ownerRegion.Value,
                    currentRegion.Value);
                return Results.Json(
                    new { status = "rejected", ownerRegion = ownerRegion.Value, currentRegion = currentRegion.Value, pid },
                    statusCode: StatusCodes.Status421MisdirectedRequest);
            }

            app.Logger.LogInformation(
                "[{Region}] Write ACEPTADO para tenant {TenantId} (pid={Pid}).",
                currentRegion.Value,
                tenantId,
                pid);
            return Results.Ok(new { status = "accepted", region = currentRegion.Value, pid });
        });

        app.Logger.LogInformation(
            "Nodo región '{Region}' arrancado. PID={Pid} puerto={Port} mapFile={MapFile}. Ctrl+C o taskkill /F para simular caída.",
            regionId,
            pid,
            port,
            Path.GetFullPath(mapFile));

        await app.RunAsync();
    }

    // ---------------------------------------------------------------------------------------------
    // failover: workflow de promoción con controles de seguridad (F5-10).
    //
    // Salvaguarda 1 (quórum/confirmación explícita): SIEMPRE requiere --confirm. Sin ese flag el
    // comando aborta y registra el intento en el log de auditoría -- nadie promueve por accidente
    // corriendo el comando sin el flag deliberado.
    //
    // Salvaguarda 2 (ventana de gracia ante partición transitoria): si se pasa --health-url, la
    // promoción SOLO ocurre si se observan <threshold> fallos de health-check CONSECUTIVOS (separados
    // por --interval-seconds); un único fallo aislado (blip transitorio de red) nunca dispara la
    // promoción, incluso con --confirm presente.
    //
    // Repetibilidad: la operación es IDEMPOTENTE (promover al mismo propietario ya vigente es un no-op
    // auditado, no reescribe el archivo) y REVERSIBLE (failback es la misma operación con los
    // parámetros invertidos) -- exactamente lo que permite ejecutar failover -> failback -> failover de
    // nuevo sin dejar el mapa en un estado inconsistente (criterio de aceptación "Ejecución repetible").
    // ---------------------------------------------------------------------------------------------

    private static async Task<int> RunFailoverAsync(
        string mapFile,
        string auditLog,
        string tenantId,
        string candidateRegion,
        string[] options)
    {
        string? healthUrl = null;
        var threshold = 3;
        var intervalSeconds = 2;
        var confirm = false;

        for (var i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "--health-url":
                    healthUrl = options[++i];
                    break;
                case "--threshold":
                    threshold = int.Parse(options[++i]);
                    break;
                case "--interval-seconds":
                    intervalSeconds = int.Parse(options[++i]);
                    break;
                case "--confirm":
                    confirm = true;
                    break;
            }
        }

        var consecutiveFailures = 0;

        if (healthUrl is not null)
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

            for (var attempt = 1; attempt <= threshold; attempt++)
            {
                var healthy = await IsHealthyAsync(httpClient, healthUrl);
                if (healthy)
                {
                    Console.WriteLine($"failover: health-check {attempt}/{threshold} OK ({healthUrl}) -- región candidata a caer está sana, NO se promueve.");
                    AppendAudit(auditLog, tenantId, previousOwner: null, newOwner: candidateRegion, result: "aborted-healthy",
                        consecutiveFailures: 0, healthUrl, reason: "health-check-ok");
                    return 2;
                }

                consecutiveFailures++;
                Console.WriteLine($"failover: health-check {attempt}/{threshold} FALLÓ ({healthUrl}) -- fallos consecutivos={consecutiveFailures}.");

                if (consecutiveFailures < threshold && attempt < threshold)
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
                }
            }

            if (consecutiveFailures < threshold)
            {
                Console.WriteLine("failover: umbral de fallos consecutivos NO alcanzado -- se aborta la promoción (ventana de gracia, evita actuar ante un blip transitorio).");
                AppendAudit(auditLog, tenantId, previousOwner: null, newOwner: candidateRegion, result: "aborted-threshold-not-met",
                    consecutiveFailures, healthUrl, reason: "insufficient-consecutive-failures");
                return 2;
            }
        }

        if (!confirm)
        {
            Console.WriteLine("failover: falta --confirm (confirmación explícita obligatoria) -- se aborta la promoción sin tocar el mapa.");
            AppendAudit(auditLog, tenantId, previousOwner: null, newOwner: candidateRegion, result: "aborted-not-confirmed",
                consecutiveFailures, healthUrl, reason: "missing-explicit-confirmation");
            return 3;
        }

        var previousOwner = PromoteTenant(mapFile, tenantId, candidateRegion, out var promoted);

        if (!promoted)
        {
            Console.WriteLine($"failover: tenant '{tenantId}' ya tiene a '{candidateRegion}' como propietaria -- no-op idempotente, el mapa no se reescribe.");
            AppendAudit(auditLog, tenantId, previousOwner, newOwner: candidateRegion, result: "no-op-already-owner",
                consecutiveFailures, healthUrl, reason: healthUrl is null ? "manual-confirmed" : "consecutive-health-check-failures");
            return 0;
        }

        Console.WriteLine($"failover: PROMOCIÓN aplicada. tenant='{tenantId}' propietaria anterior='{previousOwner ?? RegionId.Primary.Value}' propietaria nueva='{candidateRegion}'.");
        AppendAudit(auditLog, tenantId, previousOwner, newOwner: candidateRegion, result: "promoted",
            consecutiveFailures, healthUrl, reason: healthUrl is null ? "manual-confirmed" : "consecutive-health-check-failures");
        return 0;
    }

    private static async Task<bool> IsHealthyAsync(HttpClient httpClient, string healthUrl)
    {
        try
        {
            var response = await httpClient.GetAsync(healthUrl);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            // Timeout, conexión rechazada, host caído -- cualquier excepción de transporte cuenta como
            // fallo del health-check, igual que un monitor de infraestructura real.
            return false;
        }
    }

    /// <summary>
    /// Reescritura atómica del mapa (write-to-temp + <see cref="File.Move(string, string, bool)"/>)
    /// para que ningún proceso "node" que esté observando el archivo con
    /// <c>reloadOnChange: true</c> pueda leer un JSON a medio escribir.
    /// </summary>
    private static string? PromoteTenant(string mapFile, string tenantId, string candidateRegion, out bool promoted)
    {
        var doc = File.Exists(mapFile)
            ? JsonNode.Parse(File.ReadAllText(mapFile)) as JsonObject ?? new JsonObject()
            : new JsonObject();

        var mapNode = doc["TenantRegionMap"] as JsonObject ?? new JsonObject();
        var previousOwner = mapNode[tenantId]?.GetValue<string>();

        if (previousOwner == candidateRegion)
        {
            promoted = false;
            return previousOwner;
        }

        mapNode[tenantId] = candidateRegion;
        doc["TenantRegionMap"] = mapNode;

        var tempFile = mapFile + ".tmp";
        File.WriteAllText(tempFile, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempFile, mapFile, overwrite: true);

        promoted = true;
        return previousOwner;
    }

    private static void AppendAudit(
        string auditLog,
        string tenantId,
        string? previousOwner,
        string newOwner,
        string result,
        int consecutiveFailures,
        string? healthUrl,
        string reason)
    {
        var entry = new JsonObject
        {
            ["timestampUtc"] = DateTime.UtcNow.ToString("O"),
            ["tenantId"] = tenantId,
            ["previousOwner"] = previousOwner,
            ["newOwner"] = newOwner,
            ["result"] = result,
            ["reason"] = reason,
            ["consecutiveFailures"] = consecutiveFailures,
            ["healthUrl"] = healthUrl,
            ["triggeredBy"] = Environment.UserName,
            ["pid"] = Environment.ProcessId,
            ["machine"] = Environment.MachineName,
        };

        File.AppendAllText(auditLog, entry.ToJsonString() + Environment.NewLine);
    }

    private static void PrintStatus(string mapFile, string auditLog)
    {
        Console.WriteLine($"--- Mapa vigente ({mapFile}) ---");
        Console.WriteLine(File.Exists(mapFile) ? File.ReadAllText(mapFile) : "(no existe)");

        Console.WriteLine($"--- Auditoría ({auditLog}) ---");
        Console.WriteLine(File.Exists(auditLog) ? File.ReadAllText(auditLog) : "(sin entradas)");
    }
}

internal static class RegionalFailoverConstants
{
    public const string OwnerRegionHeaderName = "X-BitCode-Owner-Region";
}
