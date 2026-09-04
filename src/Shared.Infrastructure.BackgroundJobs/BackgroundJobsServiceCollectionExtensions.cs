using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace BitCode.Framework.Shared.Infrastructure.BackgroundJobs;

public static class BackgroundJobsServiceCollectionExtensions
{
    /// <summary>
    /// Registra Quartz.NET con el hosted service que ejecuta el scheduler, esperando a que los
    /// jobs en curso terminen antes de apagar el proceso (WaitForJobsToComplete). El proyecto
    /// consumidor define sus propios IJob y triggers dentro de `configureJobs`, p.ej.:
    ///   configureJobs.AddJob&lt;MiJob&gt;(j => j.WithIdentity("mi-job"))
    ///     .AddTrigger(t => t.ForJob("mi-job").WithCronSchedule("0 0 * * * ?"));
    /// </summary>
    public static IServiceCollection AddSharedBackgroundJobs(
        this IServiceCollection services,
        Action<IServiceCollectionQuartzConfigurator> configureJobs)
    {
        services.AddQuartz(configureJobs);
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        return services;
    }
}
