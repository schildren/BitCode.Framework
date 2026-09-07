namespace BitCode.Framework.Shared.Infrastructure.BackgroundJobs;

/// <summary>
/// Opciones para habilitar el <c>AdoJobStore</c> persistente de Quartz.NET sobre SQL Server con
/// clustering (F4-11). Pasar una instancia configurada a
/// <see cref="BackgroundJobsServiceCollectionExtensions.AddSharedBackgroundJobs"/> convierte el
/// scheduler de "en memoria, un único proceso" a "persistente, coordinado entre N réplicas
/// apuntando al mismo SQL Server" — el mecanismo que garantiza que un job lógico programado
/// (mismo <c>JobKey</c>/<c>TriggerKey</c>) se dispare una sola vez entre todas las instancias.
/// </summary>
public sealed class QuartzHighAvailabilityOptions
{
    /// <summary>
    /// Cadena de conexión a SQL Server donde viven las tablas <c>QRTZ_*</c>. Obligatoria — sin
    /// ella no tiene sentido invocar el overload de persistencia de
    /// <see cref="BackgroundJobsServiceCollectionExtensions.AddSharedBackgroundJobs"/>.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Prefijo de las tablas <c>QRTZ_*</c>. Coincide con el prefijo usado por el esquema
    /// versionado en <c>Schema/quartz-sqlserver-schema.sql</c> — cambiarlo requiere aplicar ese
    /// script con el mismo prefijo distinto (no soportado por
    /// <see cref="QuartzSqlServerSchemaInitializer"/>, que asume <c>QRTZ_</c>).
    /// </summary>
    public string TablePrefix { get; set; } = "QRTZ_";

    /// <summary>
    /// Nombre lógico del scheduler (<c>quartz.scheduler.instanceName</c>). Debe ser IDÉNTICO en
    /// todas las réplicas que deban coordinarse entre sí — Quartz agrupa instancias en el mismo
    /// cluster por <c>(SchedulerName, filas QRTZ_SCHEDULER_STATE)</c>, no por configuración de red.
    /// <c>null</c> conserva el valor por defecto de Quartz.NET (<c>"QuartzScheduler"</c>), que ya
    /// es estable entre procesos de un mismo binario — solo hace falta fijarlo explícitamente si
    /// un mismo proceso registra más de un scheduler o si dos aplicaciones distintas comparten la
    /// misma base de datos de Quartz y deben mantenerse en clusters separados.
    /// </summary>
    public string? SchedulerName { get; set; }

    /// <summary>
    /// Habilita <c>quartz.jobStore.clustered = true</c>. <c>true</c> por defecto: el propósito de
    /// esta tarea (F4-11) es justamente que múltiples réplicas compartan el mismo scheduler lógico
    /// sin duplicar ejecuciones. Ponerlo en <c>false</c> es válido solo para un despliegue de una
    /// única instancia que igual quiere persistencia (recuperar el estado tras un reinicio del
    /// proceso), pero sin coordinación entre réplicas — no debe usarse con más de una réplica activa.
    /// </summary>
    public bool Clustered { get; set; } = true;

    /// <summary>
    /// <c>quartz.jobStore.misfireThreshold</c>: cuánto tiempo después de la hora programada un
    /// trigger que no pudo dispararse a tiempo (nodo caído, scheduler saturado) se considera
    /// "misfire" y aplica la política de misfire del trigger en vez de disparar inmediatamente.
    /// Default 60s, razonable para jobs con cadencia de minutos u horas — un job con cadencia de
    /// segundos debe bajar este valor explícitamente.
    /// </summary>
    public TimeSpan MisfireThreshold { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// <c>quartz.jobStore.clusterCheckinInterval</c>: cada cuánto un nodo "hace check-in" en
    /// <c>QRTZ_SCHEDULER_STATE</c> para anunciar que sigue vivo. Los demás nodos usan este valor
    /// (junto con <see cref="ClusterCheckinMisfireThreshold"/>) para decidir cuándo un nodo se
    /// considera caído y recuperar sus triggers/jobs en vuelo (<c>RequestsRecovery</c>).
    /// </summary>
    public TimeSpan ClusterCheckinInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// <c>quartz.jobStore.clusterCheckinMisfireThreshold</c>: margen adicional sobre
    /// <see cref="ClusterCheckinInterval"/> antes de que otro nodo considere a un nodo sin
    /// check-in reciente como caído. Debe ser mayor que <see cref="ClusterCheckinInterval"/> para
    /// tolerar variabilidad normal de red/GC sin disparar recovery falsos positivos.
    /// </summary>
    public TimeSpan ClusterCheckinMisfireThreshold { get; set; } = TimeSpan.FromSeconds(20);
}
