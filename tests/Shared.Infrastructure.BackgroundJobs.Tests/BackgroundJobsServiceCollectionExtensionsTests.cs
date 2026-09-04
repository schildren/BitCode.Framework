using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;

namespace BitCode.Framework.Shared.Infrastructure.BackgroundJobs.Tests;

public class BackgroundJobsServiceCollectionExtensionsTests
{
    // Un único test/Host: Quartz.Logging.LogProvider cachea el ILoggerFactory del primer Host
    // creado en el proceso de forma estática (Quartz.Extensions.Hosting no lo libera al disponer
    // el Host), así que crear un segundo Host en el mismo proceso de test lanza
    // ObjectDisposedException al referenciar el LoggerFactory ya liberado del primero.
    [Fact]
    public async Task AddSharedBackgroundJobs_RegistersSchedulerAndRunsRegisteredJobOnSchedule()
    {
        var executed = new TaskCompletionSource();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(executed);
                services.AddSharedBackgroundJobs(quartz =>
                {
                    var jobKey = new JobKey("probe-job");
                    quartz.AddJob<ProbeJob>(job => job.WithIdentity(jobKey));
                    quartz.AddTrigger(trigger => trigger
                        .ForJob(jobKey)
                        .StartNow());
                });
            })
            .Build();

        var schedulerFactory = host.Services.GetRequiredService<ISchedulerFactory>();
        (await schedulerFactory.GetScheduler()).Should().NotBeNull();

        await host.StartAsync();
        var completed = await Task.WhenAny(executed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        await host.StopAsync();

        completed.Should().Be(executed.Task, "el job debió ejecutarse dentro del tiempo de espera");
    }
}
