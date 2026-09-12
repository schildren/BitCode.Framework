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
    /// <param name="configureHighAvailability">
    /// F4-11: cuando se provee, cambia el <c>JobStore</c> de <c>RAMJobStore</c> (default, en
    /// memoria de un único proceso) a <c>AdoJobStore</c> persistente sobre SQL Server, con
    /// clustering habilitado por defecto (<see cref="QuartzHighAvailabilityOptions.Clustered"/>).
    /// Con más de una réplica del proceso apuntando a la misma base de datos, esto es lo que hace
    /// que un job lógico (mismo <c>JobKey</c>/<c>TriggerKey</c>) se dispare una sola vez entre
    /// todas ellas, en vez de una vez POR réplica. El esquema <c>QRTZ_*</c> debe existir de
    /// antemano — ver <see cref="QuartzSqlServerSchemaInitializer"/> y
    /// <c>docs/guia-quartz-ha.md</c>. Omitir este parámetro conserva el comportamiento anterior
    /// (RAMJobStore, apto solo para un único proceso o para pruebas).
    /// </param>
    public static IServiceCollection AddSharedBackgroundJobs(
        this IServiceCollection services,
        Action<IServiceCollectionQuartzConfigurator> configureJobs,
        Action<QuartzHighAvailabilityOptions>? configureHighAvailability = null)
    {
        services.AddQuartz(quartz =>
        {
            if (configureHighAvailability is not null)
            {
                ConfigurePersistentClusteredStore(quartz, configureHighAvailability);
            }

            configureJobs(quartz);
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        return services;
    }

    private static void ConfigurePersistentClusteredStore(
        IServiceCollectionQuartzConfigurator quartz,
        Action<QuartzHighAvailabilityOptions> configureHighAvailability)
    {
        var options = new QuartzHighAvailabilityOptions();
        configureHighAvailability(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                $"{nameof(QuartzHighAvailabilityOptions)}.{nameof(QuartzHighAvailabilityOptions.ConnectionString)} " +
                "es obligatorio para habilitar el JobStore persistente de Quartz (AdoJobStore) — sin él no hay " +
                "ninguna base de datos donde coordinar el clustering entre réplicas.");
        }

        if (!string.IsNullOrWhiteSpace(options.SchedulerName))
        {
            quartz.SchedulerName = options.SchedulerName;
        }

        // "AUTO" es el valor reservado de Quartz.NET para que cada proceso genere su propio
        // instanceId (host + timestamp + secuencia) al arrancar — obligatorio para clustering:
        // dos nodos con el mismo instanceId corromperían la coordinación de QRTZ_SCHEDULER_STATE.
        quartz.SchedulerId = "AUTO";
        quartz.MisfireThreshold = options.MisfireThreshold;

        quartz.UsePersistentStore(store =>
        {
            store.PerformSchemaValidation = true;
            store.UseProperties = true;
            store.RetryInterval = TimeSpan.FromSeconds(15);

            store.UseSqlServer(sqlServer =>
            {
                sqlServer.ConnectionString = options.ConnectionString;
                sqlServer.TablePrefix = options.TablePrefix;
            });

            store.UseSystemTextJsonSerializer();

            if (options.Clustered)
            {
                store.UseClustering(cluster =>
                {
                    cluster.CheckinInterval = options.ClusterCheckinInterval;
                    cluster.CheckinMisfireThreshold = options.ClusterCheckinMisfireThreshold;
                });
            }
        });
    }
}
