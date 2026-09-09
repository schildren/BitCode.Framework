using System.Linq;
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
/// de ejecución repetida (failover → failback → failover) obtenida con esta herramienta, y
/// <c>docs/failback-fase5.md</c> (F5-11) para el comando <c>failback</c> abajo: a diferencia de
/// <c>failover</c> (que también sirve como "failback manual" cuando no hay <c>--health-url</c>,
/// según F5-10 sección 2.4), <c>failback</c> NUNCA revierte sin evidencia de resincronización
/// (<c>--resync-url</c> es obligatorio, nunca opcional) y aplica una ventana explícita de
/// no-doble-escritor (todas las regiones rechazan escrituras del tenant) antes de promover la
/// región que vuelve — esto es lo que evita split-brain, a diferencia del simple "no-op si ya es
/// propietario" de <c>failover</c>.
/// <code>
///   RegionalFailoverHarness.exe init       &lt;mapFile&gt; &lt;tenantId&gt; &lt;ownerRegion&gt;
///   RegionalFailoverHarness.exe node       &lt;mapFile&gt; &lt;regionId&gt; &lt;port&gt; [replicationStateFile] [ledgerFile]
///   RegionalFailoverHarness.exe resync-set &lt;replicationStateFile&gt; &lt;tenantId&gt; &lt;lagSeconds&gt;
///   RegionalFailoverHarness.exe failover   &lt;mapFile&gt; &lt;auditLog&gt; &lt;tenantId&gt; &lt;candidateRegion&gt;
///                                          [--health-url &lt;url&gt;] [--threshold &lt;n&gt;]
///                                          [--interval-seconds &lt;s&gt;] [--confirm]
///   RegionalFailoverHarness.exe failback   &lt;mapFile&gt; &lt;auditLog&gt; &lt;tenantId&gt; &lt;candidateRegion&gt;
///                                          --resync-url &lt;url&gt; [--resync-threshold &lt;n&gt;]
///                                          [--resync-interval-seconds &lt;s&gt;] [--lock-hold-seconds &lt;s&gt;]
///                                          [--confirm]
///   RegionalFailoverHarness.exe status     &lt;mapFile&gt; &lt;auditLog&gt;
///   RegionalFailoverHarness.exe replicate  &lt;sourceLedger&gt; &lt;destLedger&gt; &lt;delaySeconds&gt;
///   RegionalFailoverHarness.exe ledger-diff &lt;ledgerA&gt; &lt;ledgerB&gt;
/// </code>
/// Los dos últimos comandos son de F5-12 (DR drills): <c>replicate</c> simula, con procesos de SO
/// reales, el mismo fenómeno que F5-04 (log shipping)/F5-05 (mirror Kafka cross-cluster) miden con
/// infraestructura real (SQL Server/Kafka vía Testcontainers) -- una cola de escrituras confirmadas
/// en el origen que tarda <c>delaySeconds</c> reales en aparecer en el destino -- para poder medir un
/// RPO real (segundos de escritura efectivamente perdidos) en un simulacro end-to-end que combina
/// <c>failover</c> + <c>failback</c> sin depender de esa topología. <c>ledger-diff</c> calcula ese RPO
/// real comparando los dos ledgers en el instante de la caída. Ver <c>docs/dr-drill-fase5.md</c>.
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // F5-12: la consola de Windows por defecto no es UTF-8 -- sin esto, los acentos de los
        // mensajes (ya existentes desde F5-10/F5-11) se ven corruptos al correr el drill end-to-end
        // y redirigir la salida a un log de texto.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

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
                await RunNodeAsync(
                    args[1],
                    args[2],
                    int.Parse(args[3]),
                    args.Length >= 5 ? args[4] : null,
                    args.Length >= 6 ? args[5] : null);
                return 0;

            case "resync-set" when args.Length >= 4:
                SetReplicationLag(args[1], args[2], double.Parse(args[3]));
                return 0;

            case "failover" when args.Length >= 4:
                return await RunFailoverAsync(args[1], args[2], args[3], args[4], args[5..]);

            case "failback" when args.Length >= 4:
                return await RunFailbackAsync(args[1], args[2], args[3], args[4], args[5..]);

            case "status" when args.Length >= 3:
                PrintStatus(args[1], args[2]);
                return 0;

            case "replicate" when args.Length >= 4:
                await RunReplicateAsync(args[1], args[2], double.Parse(args[3]));
                return 0;

            case "ledger-diff" when args.Length >= 3:
                PrintLedgerDiff(args[1], args[2]);
                return 0;

            default:
                PrintUsage();
                return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Uso:");
        Console.WriteLine("  RegionalFailoverHarness init       <mapFile> <tenantId> <ownerRegion>");
        Console.WriteLine("  RegionalFailoverHarness node       <mapFile> <regionId> <port> [replicationStateFile] [ledgerFile]");
        Console.WriteLine("  RegionalFailoverHarness resync-set <replicationStateFile> <tenantId> <lagSeconds>");
        Console.WriteLine("  RegionalFailoverHarness failover   <mapFile> <auditLog> <tenantId> <candidateRegion>");
        Console.WriteLine("                                     [--health-url <url>] [--threshold <n>]");
        Console.WriteLine("                                     [--interval-seconds <s>] [--confirm]");
        Console.WriteLine("  RegionalFailoverHarness failback   <mapFile> <auditLog> <tenantId> <candidateRegion>");
        Console.WriteLine("                                     --resync-url <url> [--resync-threshold <n>]");
        Console.WriteLine("                                     [--resync-interval-seconds <s>] [--lock-hold-seconds <s>]");
        Console.WriteLine("                                     [--confirm]");
        Console.WriteLine("  RegionalFailoverHarness status     <mapFile> <auditLog>");
        Console.WriteLine("  RegionalFailoverHarness replicate  <sourceLedger> <destLedger> <delaySeconds>");
        Console.WriteLine("  RegionalFailoverHarness ledger-diff <ledgerA> <ledgerB>");
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

    private static async Task RunNodeAsync(string mapFile, string regionId, int port, string? replicationStateFile, string? ledgerFile)
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

        // F5-11: archivo independiente que representa, para ESTA región, cuán al día está su réplica
        // local respecto de la región que fue temporalmente propietaria durante el failover (en un
        // despliegue real esto lo alimentaría la métrica de RPO real de F5-04/F5-05, p. ej.
        // SqlLogShippingRpoIntegrationTests o el lag de consumidor de Kafka -- acá se simula con
        // "resync-set" para poder ejercer el escenario adversarial con procesos de SO reales).
        if (replicationStateFile is not null)
        {
            builder.Configuration.AddJsonFile(replicationStateFile, optional: true, reloadOnChange: true);
        }

        var app = builder.Build();
        var currentRegion = new RegionId(regionId);
        var pid = Environment.ProcessId;

        app.MapGet("/health", () => Results.Ok(new { region = currentRegion.Value, pid, status = "up" }));

        app.MapGet("/replication-status/{tenantId}", (string tenantId) =>
        {
            var lagValue = app.Configuration[$"ReplicationLagSeconds:{tenantId}"];

            // Fallo cerrado (fail-closed): si no hay dato de lag explícito, NUNCA se asume "al día" --
            // una región recién recuperada, sin evidencia de resincronización, no puede reclamar que
            // está lista para el failback. Esto es lo que impide un failback prematuro por omisión.
            var lagSeconds = double.TryParse(lagValue, out var parsed) ? parsed : double.MaxValue;
            var caughtUp = lagSeconds <= 0;

            return Results.Ok(new { region = currentRegion.Value, pid, tenantId, lagSeconds, caughtUp });
        });

        app.MapGet("/write/{tenantId}", (string tenantId, HttpContext context) =>
        {
            // F5-12 (DR drill): número de secuencia opcional que el CLIENTE del drill asigna a cada
            // intento de escritura (?seq=N). Es el propio drill, no el servidor, quien lleva la cuenta
            // de qué escribió y cuándo -- así el ledger es la evidencia de lo que el CLIENTE observó
            // como confirmado, exactamente el dato que un simulacro de RPO necesita.
            var seqValue = context.Request.Query["seq"].ToString();
            var hasSeq = long.TryParse(seqValue, out var seq);
            // F5-11: ventana explícita de no-doble-escritor. Mientras el lock está activo, TODAS las
            // regiones (la propietaria vigente y la candidata en resincronización) rechazan la
            // escritura -- nunca hay ambigüedad entre "dos propietarias" ni una ventana en la que una
            // región acepte escrituras basándose en un estado de ownership que ya está siendo revisado.
            var lockedValue = app.Configuration[$"TenantRegionLock:{tenantId}"];
            if (string.Equals(lockedValue, "true", StringComparison.OrdinalIgnoreCase))
            {
                app.Logger.LogWarning(
                    "[{Region}] Write BLOQUEADO para tenant {TenantId}: ventana de no-doble-escritor activa (failback en curso).",
                    currentRegion.Value,
                    tenantId);
                return Results.Json(
                    new { status = "locked", region = currentRegion.Value, pid },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

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

            var acceptedAtUtc = DateTime.UtcNow;

            if (ledgerFile is not null && hasSeq)
            {
                // F5-12: cada escritura ACEPTADA por la región propietaria es un "evento confirmado"
                // -- el equivalente, a efectos de este drill, de un commit real en SQL/Kafka. El
                // comando "replicate" copia estas líneas hacia el ledger de la otra región con un
                // retraso real (async), y "ledger-diff" mide cuántas de estas líneas seguían sin
                // replicar en el instante de una caída real -- eso es el RPO medido del simulacro.
                AppendLedgerEntry(ledgerFile, seq, acceptedAtUtc, tenantId, currentRegion.Value);
            }

            app.Logger.LogInformation(
                "[{Region}] Write ACEPTADO para tenant {TenantId} (pid={Pid}, seq={Seq}).",
                currentRegion.Value,
                tenantId,
                pid,
                hasSeq ? seq : -1);
            return Results.Ok(new { status = "accepted", region = currentRegion.Value, pid, seq = hasSeq ? seq : (long?)null, timestampUtc = acceptedAtUtc.ToString("O") });
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

    // ---------------------------------------------------------------------------------------------
    // resync-set: actualiza (reescritura atómica) el lag de replicación simulado de una región para
    // un tenant. En un despliegue real esto NO es un comando manual -- lo alimentaría la métrica de
    // RPO real (F5-04 log shipping / Always On, F5-05 lag de consumidor Kafka). Se expone como
    // comando explícito acá únicamente para poder reproducir el escenario de resincronización con
    // procesos de SO reales sin depender de una topología SQL/Kafka completa en este harness.
    // ---------------------------------------------------------------------------------------------

    private static void SetReplicationLag(string replicationStateFile, string tenantId, double lagSeconds)
    {
        var doc = File.Exists(replicationStateFile)
            ? JsonNode.Parse(File.ReadAllText(replicationStateFile)) as JsonObject ?? new JsonObject()
            : new JsonObject();

        var lagNode = doc["ReplicationLagSeconds"] as JsonObject ?? new JsonObject();
        lagNode[tenantId] = lagSeconds;
        doc["ReplicationLagSeconds"] = lagNode;

        var tempFile = replicationStateFile + ".tmp";
        File.WriteAllText(tempFile, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempFile, replicationStateFile, overwrite: true);

        Console.WriteLine($"resync-set: '{replicationStateFile}' tenant='{tenantId}' lagSeconds={lagSeconds} (caughtUp={lagSeconds <= 0}).");
    }

    // ---------------------------------------------------------------------------------------------
    // failback: workflow de RETORNO de ownership hacia una región que fue temporalmente desplazada
    // por un failover (F5-11). Deliberadamente NO es el mismo código que "failover" con los
    // parámetros invertidos (a diferencia del failback MANUAL descrito en la sección 4.3 de
    // docs/failover-automatizado-fase5.md, que es una decisión de operador sin evidencia de
    // resincronización automatizable): un failback siempre corre el riesgo de que la región que
    // vuelve tenga datos desactualizados respecto de la región que estuvo aceptando escrituras
    // mientras la primera estaba caída. Revertir sin verificar eso es split-brain de datos, no solo
    // de tráfico. Dos garantías obligatorias, ninguna opcional:
    //
    // 1) Resincronización verificada ANTES de revertir (--resync-url es OBLIGATORIO, nunca opcional
    //    como --health-url en "failover"): solo se promueve a la región candidata cuando su propio
    //    endpoint de estado de replicación reporta "al día" (caughtUp=true) en <resync-threshold>
    //    observaciones CONSECUTIVAS -- igual patrón de ventana de gracia que "failover", pero
    //    exigiendo éxito consecutivo en vez de fallo consecutivo, y sin poder saltearse el chequeo.
    //
    // 2) Ventana explícita de no-doble-escritor: antes de reescribir el propietario, se activa un
    //    lock (`TenantRegionLock:{tenantId}=true`) que TODAS las instancias "node" (incluida la
    //    propietaria vigente) observan vía el mismo hot-reload de F4-12 y usan para rechazar
    //    CUALQUIER escritura del tenant, sin ambigüedad: nunca hay un instante en el que dos
    //    regiones puedan aceptar escrituras del mismo tenant simultáneamente, y nunca hay un
    //    instante en el que ninguna región sepa quién es la propietaria real -- o hay exactamente
    //    una propietaria, o hay un bloqueo total y corto, nunca un estado ambiguo.
    // ---------------------------------------------------------------------------------------------

    private static async Task<int> RunFailbackAsync(
        string mapFile,
        string auditLog,
        string tenantId,
        string candidateRegion,
        string[] options)
    {
        string? resyncUrl = null;
        var resyncThreshold = 3;
        var resyncIntervalSeconds = 2;
        var lockHoldSeconds = 2;
        var confirm = false;

        for (var i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "--resync-url":
                    resyncUrl = options[++i];
                    break;
                case "--resync-threshold":
                    resyncThreshold = int.Parse(options[++i]);
                    break;
                case "--resync-interval-seconds":
                    resyncIntervalSeconds = int.Parse(options[++i]);
                    break;
                case "--lock-hold-seconds":
                    lockHoldSeconds = int.Parse(options[++i]);
                    break;
                case "--confirm":
                    confirm = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(resyncUrl))
        {
            Console.WriteLine("failback: falta --resync-url (obligatorio) -- nunca se revierte ownership sin evidencia de resincronización. Se aborta sin tocar el mapa.");
            AppendAudit(auditLog, tenantId, previousOwner: null, newOwner: candidateRegion, result: "aborted-missing-resync-url",
                consecutiveFailures: 0, healthUrl: null, reason: "missing-resync-url");
            return 4;
        }

        var consecutiveCaughtUp = 0;

        using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
        {
            for (var attempt = 1; attempt <= resyncThreshold; attempt++)
            {
                var caughtUp = await IsCaughtUpAsync(httpClient, resyncUrl, tenantId);

                if (caughtUp)
                {
                    consecutiveCaughtUp++;
                    Console.WriteLine($"failback: resync-check {attempt}/{resyncThreshold} AL DÍA ({resyncUrl}) -- consecutivos al día={consecutiveCaughtUp}.");
                }
                else
                {
                    consecutiveCaughtUp = 0;
                    Console.WriteLine($"failback: resync-check {attempt}/{resyncThreshold} NO al día ({resyncUrl}) -- se reinicia el contador de observaciones consecutivas.");
                }

                if (consecutiveCaughtUp < resyncThreshold && attempt < resyncThreshold)
                {
                    await Task.Delay(TimeSpan.FromSeconds(resyncIntervalSeconds));
                }
            }

            if (consecutiveCaughtUp < resyncThreshold)
            {
                Console.WriteLine("failback: la región candidata NO demostró estar resincronizada -- se aborta la reversión sin tocar el mapa (evita split-brain de datos).");
                AppendAudit(auditLog, tenantId, previousOwner: null, newOwner: candidateRegion, result: "aborted-not-resynced",
                    consecutiveFailures: 0, healthUrl: resyncUrl, reason: "insufficient-consecutive-resync-confirmations");
                return 2;
            }
        }

        if (!confirm)
        {
            Console.WriteLine("failback: falta --confirm (confirmación explícita obligatoria) -- se aborta la reversión sin tocar el mapa.");
            AppendAudit(auditLog, tenantId, previousOwner: null, newOwner: candidateRegion, result: "aborted-not-confirmed",
                consecutiveFailures: 0, healthUrl: resyncUrl, reason: "missing-explicit-confirmation");
            return 3;
        }

        var currentOwner = ReadOwner(mapFile, tenantId);

        if (currentOwner == candidateRegion)
        {
            Console.WriteLine($"failback: tenant '{tenantId}' ya tiene a '{candidateRegion}' como propietaria -- no-op idempotente, el mapa no se reescribe y no se activa el lock.");
            AppendAudit(auditLog, tenantId, previousOwner: currentOwner, newOwner: candidateRegion, result: "no-op-already-owner",
                consecutiveFailures: 0, healthUrl: resyncUrl, reason: "resync-confirmed-failback");
            return 0;
        }

        // Ventana de no-doble-escritor: se activa el lock ANTES de tocar el propietario. Desde este
        // instante y hasta que se libere (unido, en la misma escritura atómica, a la promoción), toda
        // instancia "node" -- la propietaria vigente incluida -- rechaza escrituras del tenant.
        SetLock(mapFile, tenantId, locked: true);
        Console.WriteLine($"failback: LOCK activado para tenant '{tenantId}' -- ninguna región acepta escrituras durante {lockHoldSeconds}s (ventana de no-doble-escritor).");
        AppendAudit(auditLog, tenantId, previousOwner: currentOwner, newOwner: candidateRegion, result: "lock-applied",
            consecutiveFailures: consecutiveCaughtUp, healthUrl: resyncUrl, reason: "no-double-writer-window");

        await Task.Delay(TimeSpan.FromSeconds(lockHoldSeconds));

        PromoteAndUnlock(mapFile, tenantId, candidateRegion);

        Console.WriteLine($"failback: PROMOCIÓN aplicada y lock liberado. tenant='{tenantId}' propietaria anterior='{currentOwner ?? RegionId.Primary.Value}' propietaria nueva='{candidateRegion}'.");
        AppendAudit(auditLog, tenantId, previousOwner: currentOwner, newOwner: candidateRegion, result: "promoted",
            consecutiveFailures: consecutiveCaughtUp, healthUrl: resyncUrl, reason: "resync-confirmed-failback");

        return 0;
    }

    private static async Task<bool> IsCaughtUpAsync(HttpClient httpClient, string resyncUrl, string tenantId)
    {
        try
        {
            var url = resyncUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(tenantId);
            var response = await httpClient.GetAsync(url);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return false;
            }

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(body) as JsonObject;
            return json?["caughtUp"]?.GetValue<bool>() ?? false;
        }
        catch
        {
            // Igual que el health-check de "failover": cualquier fallo de transporte se trata como
            // "NO al día" -- fallo cerrado, nunca se asume resincronización sin evidencia positiva.
            return false;
        }
    }

    private static string? ReadOwner(string mapFile, string tenantId)
    {
        if (!File.Exists(mapFile))
        {
            return null;
        }

        var doc = JsonNode.Parse(File.ReadAllText(mapFile)) as JsonObject;
        var mapNode = doc?["TenantRegionMap"] as JsonObject;
        return mapNode?[tenantId]?.GetValue<string>();
    }

    /// <summary>
    /// Reescritura atómica del flag de lock del tenant, independiente de la reescritura del
    /// propietario -- así una instancia "node" nunca puede leer, a mitad de escritura, un archivo
    /// con el lock activado pero el propietario ya cambiado (o viceversa) de forma inconsistente.
    /// </summary>
    private static void SetLock(string mapFile, string tenantId, bool locked)
    {
        var doc = File.Exists(mapFile)
            ? JsonNode.Parse(File.ReadAllText(mapFile)) as JsonObject ?? new JsonObject()
            : new JsonObject();

        var lockNode = doc["TenantRegionLock"] as JsonObject ?? new JsonObject();
        lockNode[tenantId] = locked;
        doc["TenantRegionLock"] = lockNode;

        var tempFile = mapFile + ".tmp";
        File.WriteAllText(tempFile, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempFile, mapFile, overwrite: true);
    }

    /// <summary>
    /// Libera el lock y promueve al nuevo propietario en UNA sola escritura atómica -- ningún proceso
    /// "node" observando el archivo puede ver un estado intermedio de "lock liberado pero propietario
    /// todavía viejo" ni al revés.
    /// </summary>
    private static void PromoteAndUnlock(string mapFile, string tenantId, string candidateRegion)
    {
        var doc = File.Exists(mapFile)
            ? JsonNode.Parse(File.ReadAllText(mapFile)) as JsonObject ?? new JsonObject()
            : new JsonObject();

        var mapNode = doc["TenantRegionMap"] as JsonObject ?? new JsonObject();
        mapNode[tenantId] = candidateRegion;
        doc["TenantRegionMap"] = mapNode;

        var lockNode = doc["TenantRegionLock"] as JsonObject ?? new JsonObject();
        lockNode[tenantId] = false;
        doc["TenantRegionLock"] = lockNode;

        var tempFile = mapFile + ".tmp";
        File.WriteAllText(tempFile, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempFile, mapFile, overwrite: true);
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

    // ---------------------------------------------------------------------------------------------
    // F5-12 (DR drills): ledger + replicate + ledger-diff -- el mecanismo de medición REAL de RPO
    // que usa el simulacro end-to-end (docs/dr-drill-fase5.md). El ledger de una región es la lista,
    // en el orden real en que ocurrieron (append-only, con timestamp real de servidor), de las
    // escrituras que esa región aceptó como propietaria. "replicate" simula, con un delay real de
    // reloj de pared (no un reloj virtual), el mismo fenómeno asíncrono que F5-04 (log shipping) y
    // F5-05 (mirror Kafka) miden con infraestructura real -- una cola de escrituras confirmadas en el
    // origen que tarda un tiempo real en aparecer en el destino. Si el proceso "replicate" se mata en
    // el mismo instante que el nodo de la región caída (mismo "apagón" simulado), lo que no llegó a
    // copiarse todavía es, por construcción, exactamente lo que un desastre real perdería -- eso es
    // lo que "ledger-diff" cuantifica.
    // ---------------------------------------------------------------------------------------------

    private static readonly System.Threading.SemaphoreSlim LedgerAppendLock = new(1, 1);

    private static void AppendLedgerEntry(string ledgerFile, long seq, DateTime timestampUtc, string tenantId, string region)
    {
        var entry = new JsonObject
        {
            ["seq"] = seq,
            ["timestampUtc"] = timestampUtc.ToString("O"),
            ["tenantId"] = tenantId,
            ["region"] = region,
        };

        // Un único proceso "node" atiende sus propias escrituras de forma esencialmente secuencial
        // para este drill (curl secuencial desde el script orquestador) -- un lock simple in-process
        // alcanza para que dos respuestas concurrentes no corrompan la línea (no se requiere el mismo
        // nivel de atomicidad cross-proceso que el mapa de ownership, porque solo el propio proceso
        // dueño de esta región escribe en su ledger).
        LedgerAppendLock.Wait();
        try
        {
            File.AppendAllText(ledgerFile, entry.ToJsonString() + Environment.NewLine);
        }
        finally
        {
            LedgerAppendLock.Release();
        }
    }

    private static async Task RunReplicateAsync(string sourceLedger, string destLedger, double delaySeconds)
    {
        Console.WriteLine($"replicate: '{sourceLedger}' -> '{destLedger}' con retraso real de {delaySeconds}s (Ctrl+C/taskkill para simular la caída del enlace de replicación).");

        var copiedSeqs = new HashSet<long>();

        if (File.Exists(destLedger))
        {
            foreach (var line in File.ReadAllLines(destLedger))
            {
                if (JsonNode.Parse(line) is JsonObject obj && obj["seq"]?.GetValue<long>() is { } seq)
                {
                    copiedSeqs.Add(seq);
                }
            }
        }

        while (true)
        {
            if (File.Exists(sourceLedger))
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(sourceLedger);
                }
                catch (IOException)
                {
                    // El origen puede estar siendo escrito por el nodo justo en este instante --
                    // se reintenta en el próximo ciclo del loop, nunca se aborta el proceso.
                    lines = Array.Empty<string>();
                }

                foreach (var line in lines)
                {
                    if (JsonNode.Parse(line) is not JsonObject obj)
                    {
                        continue;
                    }

                    var seq = obj["seq"]!.GetValue<long>();
                    if (copiedSeqs.Contains(seq))
                    {
                        continue;
                    }

                    var writtenAtUtc = DateTime.Parse(obj["timestampUtc"]!.GetValue<string>()).ToUniversalTime();
                    var elapsed = DateTime.UtcNow - writtenAtUtc;

                    if (elapsed.TotalSeconds >= delaySeconds)
                    {
                        File.AppendAllText(destLedger, line + Environment.NewLine);
                        copiedSeqs.Add(seq);
                        Console.WriteLine($"replicate: seq={seq} replicado tras {elapsed.TotalSeconds:F2}s reales de retraso.");
                    }
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private static void PrintLedgerDiff(string ledgerA, string ledgerB)
    {
        var entriesA = ReadLedger(ledgerA);
        var entriesB = ReadLedger(ledgerB);
        var seqsB = entriesB.Select(e => e.Seq).ToHashSet();

        var lost = entriesA.Where(e => !seqsB.Contains(e.Seq)).OrderBy(e => e.Seq).ToList();
        var lastReplicated = entriesB.OrderByDescending(e => e.Seq).FirstOrDefault();
        var lastWritten = entriesA.OrderByDescending(e => e.Seq).FirstOrDefault();

        double? rpoSeconds = null;
        if (lastWritten is not null)
        {
            var referenceTimestamp = lastReplicated?.TimestampUtc ?? lost.OrderBy(e => e.Seq).FirstOrDefault()?.TimestampUtc;
            if (referenceTimestamp is not null)
            {
                rpoSeconds = (lastWritten.TimestampUtc - referenceTimestamp.Value).TotalSeconds;
            }
        }

        var report = new JsonObject
        {
            ["ledgerA"] = ledgerA,
            ["ledgerB"] = ledgerB,
            ["totalWrittenA"] = entriesA.Count,
            ["totalReplicatedB"] = entriesB.Count,
            ["lostCount"] = lost.Count,
            ["lostSeqs"] = new JsonArray(lost.Select(e => (JsonNode)e.Seq).ToArray()),
            ["lastReplicatedSeq"] = lastReplicated?.Seq,
            ["lastReplicatedTimestampUtc"] = lastReplicated?.TimestampUtc.ToString("O"),
            ["lastWrittenSeq"] = lastWritten?.Seq,
            ["lastWrittenTimestampUtc"] = lastWritten?.TimestampUtc.ToString("O"),
            ["rpoSecondsMeasured"] = rpoSeconds,
        };

        Console.WriteLine(report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<LedgerEntry> ReadLedger(string ledgerFile)
    {
        if (!File.Exists(ledgerFile))
        {
            return new List<LedgerEntry>();
        }

        var result = new List<LedgerEntry>();
        foreach (var line in File.ReadAllLines(ledgerFile))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (JsonNode.Parse(line) is JsonObject obj)
            {
                result.Add(new LedgerEntry(
                    obj["seq"]!.GetValue<long>(),
                    DateTime.Parse(obj["timestampUtc"]!.GetValue<string>()).ToUniversalTime()));
            }
        }

        return result;
    }

    private sealed record LedgerEntry(long Seq, DateTime TimestampUtc);

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
