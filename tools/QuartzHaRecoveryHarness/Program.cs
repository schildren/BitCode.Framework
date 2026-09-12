using System.Linq;
using BitCode.Framework.Shared.Infrastructure.BackgroundJobs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BitCode.Framework.Tools.QuartzHaRecoveryHarness;

/// <summary>
/// Herramienta de diagnóstico manual, NO parte de la suite automatizada, creada para cerrar el
/// pendiente documentado en <c>docs/guia-quartz-ha.md</c> sección 6.1 (F4-11): verificar con DOS
/// PROCESOS DE SISTEMA OPERATIVO REALES (no dos schedulers en el mismo proceso .NET, que es lo que
/// ya cubre <c>QuartzHighAvailabilityIntegrationTests</c>) que un job con <c>RequestRecovery()</c>
/// ejecutándose en el nodo A es retomado por el nodo B si el proceso A muere a mitad de la
/// ejecución (<c>taskkill /F</c>), sin duplicar su efecto de negocio.
/// </summary>
/// <remarks>
/// Uso (ver <c>docs/guia-quartz-ha.md</c> sección 6.1 para el procedimiento completo):
/// <code>
///   QuartzHaRecoveryHarness.exe init &lt;connectionString&gt;
///   QuartzHaRecoveryHarness.exe run  &lt;connectionString&gt; &lt;nodeLabel&gt; [workSeconds]
///   QuartzHaRecoveryHarness.exe status &lt;connectionString&gt;
/// </code>
/// <c>init</c> crea la base de datos (si no existe), aplica el esquema <c>QRTZ_*</c> y crea la
/// tabla de evidencia <c>RecoveryEvidence</c>. <c>run</c> arranca UN scheduler Quartz.NET
/// clusterizado real, apuntando a la misma cadena de conexión, y queda corriendo hasta que el
/// proceso se termine (Ctrl+C o <c>taskkill /F</c>) — se ejecuta una instancia por proceso de SO.
/// <c>status</c> imprime el contenido de <c>RecoveryEvidence</c> para verificar el resultado.
/// </remarks>
internal static class Program
{
    private const string SchedulerName = "QuartzHaRecoveryHarness";
    private static readonly JobKey ProbeJobKey = new("recovery-probe-job");
    private static readonly TriggerKey ProbeTriggerKey = new("recovery-probe-trigger");

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        var mode = args[0];
        var connectionString = args[1];

        switch (mode)
        {
            case "init":
                await InitAsync(connectionString);
                return 0;

            case "run":
                var nodeLabel = args.Length > 2 ? args[2] : Environment.MachineName;
                var workSeconds = args.Length > 3 ? int.Parse(args[3]) : 30;
                await RunNodeAsync(connectionString, nodeLabel, workSeconds);
                return 0;

            case "status":
                await PrintStatusAsync(connectionString);
                return 0;

            default:
                PrintUsage();
                return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Uso:");
        Console.WriteLine("  QuartzHaRecoveryHarness init <connectionString>");
        Console.WriteLine("  QuartzHaRecoveryHarness run  <connectionString> <nodeLabel> [workSeconds]");
        Console.WriteLine("  QuartzHaRecoveryHarness status <connectionString>");
    }

    private static async Task InitAsync(string connectionString)
    {
        await CreateDatabaseIfMissingAsync(connectionString);
        await QuartzSqlServerSchemaInitializer.ApplySchemaIfMissingAsync(connectionString);
        await CreateEvidenceTableIfMissingAsync(connectionString);
        Console.WriteLine("init: base de datos, esquema QRTZ_* y tabla RecoveryEvidence listos.");
    }

    private static async Task CreateDatabaseIfMissingAsync(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NULL
            BEGIN
                CREATE DATABASE [{databaseName}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateEvidenceTableIfMissingAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.RecoveryEvidence', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.RecoveryEvidence
                (
                    JobKey                 nvarchar(200) NOT NULL PRIMARY KEY,
                    Status                 nvarchar(20)  NOT NULL,
                    AttemptCount           int           NOT NULL,
                    StartedByInstanceId    nvarchar(200) NULL,
                    StartedByPid           int           NULL,
                    StartedAtUtc           datetime2     NULL,
                    Recovering             bit           NULL,
                    CompletedByInstanceId  nvarchar(200) NULL,
                    CompletedByPid         int           NULL,
                    CompletedAtUtc         datetime2     NULL
                );
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RunNodeAsync(string connectionString, string nodeLabel, int workSeconds)
    {
        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        hostBuilder.Logging.ClearProviders();
        hostBuilder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss.fff ";
        });
        var services = hostBuilder.Services;
        services.AddSingleton(new RecoveryProbeJobOptions(connectionString, nodeLabel, TimeSpan.FromSeconds(workSeconds)));

        services.AddSharedBackgroundJobs(
            quartz =>
            {
                quartz.AddJob<RecoveryProbeJob>(job => job
                    .WithIdentity(ProbeJobKey)
                    .RequestRecovery()
                    .StoreDurably());

                quartz.AddTrigger(trigger => trigger
                    .WithIdentity(ProbeTriggerKey)
                    .ForJob(ProbeJobKey)
                    .StartAt(DateTimeOffset.UtcNow.AddSeconds(5)));
            },
            ha =>
            {
                ha.ConnectionString = connectionString;
                ha.SchedulerName = SchedulerName;
                ha.Clustered = true;
                // Reducidos respecto del default de producción (10s/20s) para que la detección de
                // caída del nodo y el recovery sean observables en el orden de segundos durante
                // esta prueba manual — mismo criterio ya documentado en
                // QuartzHighAvailabilityIntegrationTests para la suite automatizada.
                ha.ClusterCheckinInterval = TimeSpan.FromSeconds(3);
                ha.ClusterCheckinMisfireThreshold = TimeSpan.FromSeconds(6);
                ha.MisfireThreshold = TimeSpan.FromSeconds(10);
            });

        using var host = hostBuilder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Harness");

        await host.StartAsync();

        var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        logger.LogInformation(
            "Nodo '{NodeLabel}' arrancado. PID={Pid} InstanceId={InstanceId}. Esperando disparo/recovery del job {JobKey}. Ctrl+C o taskkill /F para simular caída.",
            nodeLabel,
            Environment.ProcessId,
            scheduler.SchedulerInstanceId,
            ProbeJobKey);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutdown ordenado solicitado (Ctrl+C) — un taskkill /F real nunca llega acá.
        }

        await host.StopAsync();
    }

    private static async Task PrintStatusAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT JobKey, Status, AttemptCount, StartedByInstanceId, StartedByPid, StartedAtUtc, Recovering, CompletedByInstanceId, CompletedByPid, CompletedAtUtc FROM dbo.RecoveryEvidence";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Console.WriteLine(
                $"JobKey={reader["JobKey"]} Status={reader["Status"]} AttemptCount={reader["AttemptCount"]} " +
                $"StartedBy={reader["StartedByInstanceId"]}/pid={reader["StartedByPid"]} StartedAtUtc={reader["StartedAtUtc"]} " +
                $"Recovering={reader["Recovering"]} CompletedBy={reader["CompletedByInstanceId"]}/pid={reader["CompletedByPid"]} CompletedAtUtc={reader["CompletedAtUtc"]}");
        }
    }
}

public sealed record RecoveryProbeJobOptions(string ConnectionString, string NodeLabel, TimeSpan WorkDuration);

/// <summary>
/// Job de prueba deliberadamente idempotente (sección 5 de <c>docs/guia-quartz-ha.md</c> /
/// regla dura 28 de <c>docs/convenciones.md</c>): antes de "trabajar" comprueba si el efecto de
/// negocio (la fila <c>RecoveryEvidence</c> para este <see cref="JobKey"/>) ya quedó
/// <c>Completed</c> — si el nodo que ejecutó el job murió DESPUÉS de completar pero el registro de
/// recovery de Quartz todavía dispara un reintento en otra ventana, este job no repite el efecto.
/// El "trabajo" real es <see cref="Task.Delay"/> por <see cref="RecoveryProbeJobOptions.WorkDuration"/>
/// — el tiempo que la prueba manual usa para matar el proceso del nodo a mitad de la ejecución.
/// </summary>
public sealed class RecoveryProbeJob(RecoveryProbeJobOptions options, ILogger<RecoveryProbeJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var jobKeyName = context.JobDetail.Key.Name;
        var instanceId = context.Scheduler.SchedulerInstanceId;
        var pid = Environment.ProcessId;

        if (await IsAlreadyCompletedAsync(jobKeyName, context.CancellationToken))
        {
            logger.LogInformation(
                "[{NodeLabel}] Job {JobKey} ya está Completed en RecoveryEvidence — Execute idempotente, no repite el efecto (Recovering={Recovering}).",
                options.NodeLabel,
                jobKeyName,
                context.Recovering);
            return;
        }

        await MarkStartedAsync(jobKeyName, instanceId, pid, context.Recovering, context.CancellationToken);
        logger.LogWarning(
            "[{NodeLabel}] Job {JobKey} INICIADO (pid={Pid}, instanceId={InstanceId}, Recovering={Recovering}). Simulando trabajo real por {WorkDuration}. Este es el momento para matar el proceso si se quiere probar recovery.",
            options.NodeLabel,
            jobKeyName,
            pid,
            instanceId,
            context.Recovering,
            options.WorkDuration);

        // Deliberadamente SIN pasar context.CancellationToken acá: la caída que esta prueba
        // ejercita es taskkill /F del PROCESO completo (no una cancelación cooperativa del
        // scheduler), así que no hay ninguna forma "elegante" de interrumpir este delay desde
        // dentro del propio proceso que se está matando.
        await Task.Delay(options.WorkDuration, CancellationToken.None);

        await MarkCompletedAsync(jobKeyName, instanceId, pid, context.CancellationToken);
        logger.LogWarning(
            "[{NodeLabel}] Job {JobKey} COMPLETADO (pid={Pid}, instanceId={InstanceId}).",
            options.NodeLabel,
            jobKeyName,
            pid,
            instanceId);
    }

    private async Task<bool> IsAlreadyCompletedAsync(string jobKeyName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM dbo.RecoveryEvidence WHERE JobKey = @jobKey";
        command.Parameters.AddWithValue("@jobKey", jobKeyName);

        var status = await command.ExecuteScalarAsync(cancellationToken) as string;
        return status == "Completed";
    }

    private async Task MarkStartedAsync(
        string jobKeyName,
        string instanceId,
        int pid,
        bool recovering,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            MERGE dbo.RecoveryEvidence AS target
            USING (SELECT @jobKey AS JobKey) AS source
            ON target.JobKey = source.JobKey
            WHEN MATCHED THEN
                UPDATE SET
                    Status = 'Started',
                    AttemptCount = target.AttemptCount + 1,
                    StartedByInstanceId = @instanceId,
                    StartedByPid = @pid,
                    StartedAtUtc = SYSUTCDATETIME(),
                    Recovering = @recovering
            WHEN NOT MATCHED THEN
                INSERT (JobKey, Status, AttemptCount, StartedByInstanceId, StartedByPid, StartedAtUtc, Recovering)
                VALUES (@jobKey, 'Started', 1, @instanceId, @pid, SYSUTCDATETIME(), @recovering);
            """;
        command.Parameters.AddWithValue("@jobKey", jobKeyName);
        command.Parameters.AddWithValue("@instanceId", instanceId);
        command.Parameters.AddWithValue("@pid", pid);
        command.Parameters.AddWithValue("@recovering", recovering);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkCompletedAsync(
        string jobKeyName,
        string instanceId,
        int pid,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.RecoveryEvidence
            SET Status = 'Completed',
                CompletedByInstanceId = @instanceId,
                CompletedByPid = @pid,
                CompletedAtUtc = SYSUTCDATETIME()
            WHERE JobKey = @jobKey AND Status <> 'Completed';
            """;
        command.Parameters.AddWithValue("@jobKey", jobKeyName);
        command.Parameters.AddWithValue("@instanceId", instanceId);
        command.Parameters.AddWithValue("@pid", pid);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
