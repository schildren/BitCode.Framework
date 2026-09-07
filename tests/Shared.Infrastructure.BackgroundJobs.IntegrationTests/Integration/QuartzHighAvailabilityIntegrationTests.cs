using System.Runtime.CompilerServices;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace BitCode.Framework.Shared.Infrastructure.BackgroundJobs.IntegrationTests.Integration;

/// <summary>
/// F4-11 (Quartz HA): evidencia central del criterio de aceptación "un job lógico no duplica
/// efectos" — dos <see cref="IScheduler"/> Quartz.NET reales e independientes (dos
/// <c>ServiceProvider</c> separados, como si fueran dos réplicas de un mismo <c>Deployment</c>),
/// ambos configurados con <c>AddSharedBackgroundJobs(..., ha => ha.Clustered = true)</c> apuntando
/// al MISMO SQL Server real (Testcontainers), con el MISMO job/trigger lógico (mismo
/// <see cref="JobKey"/>/<see cref="TriggerKey"/>) registrado en ambos. Sin clustering, ambos nodos
/// dispararían el trigger de forma independiente (dos ejecuciones); con clustering, Quartz
/// coordina vía las tablas <c>QRTZ_*</c> compartidas para que solo uno de los dos lo dispare.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class QuartzHighAvailabilityIntegrationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString(
        [CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("QuartzHA", testName);

    [Fact]
    public async Task TwoClusteredSchedulers_SharingSameSqlServer_RunLogicalJobExactlyOnce()
    {
        var connectionString = BuildIsolatedConnectionString();
        await CreateDatabaseAsync(connectionString);
        await CreateTrackingTableAsync(connectionString);
        await QuartzSqlServerSchemaInitializer.ApplySchemaIfMissingAsync(connectionString);

        var jobKey = new JobKey("cluster-probe-job");
        var triggerKey = new TriggerKey("cluster-probe-trigger");

        await using var providerA = BuildProvider(connectionString, jobKey, triggerKey);
        await using var providerB = BuildProvider(connectionString, jobKey, triggerKey);

        var schedulerA = await providerA.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var schedulerB = await providerB.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            // Ambos nodos arrancan casi simultáneamente, como dos pods de un mismo Deployment
            // pasando su readiness probe al mismo tiempo — el escenario real que produciría
            // duplicación si el JobStore no coordinara entre procesos.
            await Task.WhenAll(schedulerA.Start(), schedulerB.Start());

            // El trigger dispara ~2s después del arranque (ver BuildProvider); se espera un
            // margen amplio para que cualquiera de los dos nodos lo ejecute y quede registrado.
            await WaitUntilAsync(
                () => CountExecutionsAsync(connectionString, jobKey.Name),
                count => count >= 1,
                TimeSpan.FromSeconds(20));
        }
        finally
        {
            await Task.WhenAll(
                schedulerA.Shutdown(waitForJobsToComplete: true),
                schedulerB.Shutdown(waitForJobsToComplete: true));
        }

        // Margen adicional tras el shutdown por si, en un escenario sin clustering, el segundo
        // nodo hubiera disparado su propia copia del trigger poco después de la primera.
        var executionCount = await CountExecutionsAsync(connectionString, jobKey.Name);

        executionCount.Should().Be(
            1,
            "el clustering de Quartz (quartz.jobStore.clustered=true, AdoJobStore compartido) " +
            "debe garantizar que un job lógico programado una sola vez se dispare una única vez " +
            "entre los dos nodos, no una vez por nodo");
    }

    private static ServiceProvider BuildProvider(string connectionString, JobKey jobKey, TriggerKey triggerKey)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new TrackingJobOptions(connectionString));

        services.AddSharedBackgroundJobs(
            quartz =>
            {
                quartz.AddJob<TrackingJob>(job => job
                    .WithIdentity(jobKey)
                    .RequestRecovery()
                    .StoreDurably());

                quartz.AddTrigger(trigger => trigger
                    .WithIdentity(triggerKey)
                    .ForJob(jobKey)
                    .StartAt(DateTimeOffset.UtcNow.AddSeconds(2)));
            },
            ha =>
            {
                ha.ConnectionString = connectionString;
                ha.Clustered = true;
                // Intervalos reducidos respecto del default de producción (10s/20s) para que el
                // test no tenga que esperar minutos a que el cluster se coordine.
                ha.ClusterCheckinInterval = TimeSpan.FromSeconds(1);
                ha.ClusterCheckinMisfireThreshold = TimeSpan.FromSeconds(2);
                ha.MisfireThreshold = TimeSpan.FromSeconds(5);
            });

        return services.BuildServiceProvider();
    }

    private static async Task CreateDatabaseAsync(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateTrackingTableAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE JobExecutions (
                JobName nvarchar(200) NOT NULL,
                ExecutedAtUtc datetime2 NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountExecutionsAsync(string connectionString, string jobName)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM JobExecutions WHERE JobName = @jobName";
        command.Parameters.AddWithValue("@jobName", jobName);

        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task WaitUntilAsync<T>(
        Func<Task<T>> poll,
        Func<T, bool> isDone,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (isDone(await poll()))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cts.Token).ContinueWith(_ => { });
        }
    }
}

public sealed record TrackingJobOptions(string ConnectionString);

/// <summary>
/// Job de prueba mínimo, deliberadamente idempotente en su registro (deja constancia de cada
/// disparo real vía INSERT, nunca actualiza en el lugar) para que contar filas en
/// <c>JobExecutions</c> refleje exactamente cuántas veces Quartz disparó este job lógico entre
/// todos los nodos del cluster.
/// </summary>
public sealed class TrackingJob(TrackingJobOptions options) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(context.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO JobExecutions (JobName, ExecutedAtUtc) VALUES (@jobName, SYSUTCDATETIME())";
        command.Parameters.AddWithValue("@jobName", context.JobDetail.Key.Name);

        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }
}
