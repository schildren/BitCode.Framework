namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Opciones del cache de permisos (F2-09). Configurable vía el delegado de
/// <see cref="PermissionCacheServiceCollectionExtensions.AddSharedPermissionCache"/> -- no se lee de
/// <c>IConfiguration</c> automáticamente, mismo criterio que <c>AbacOptions</c> (F2-08): son parámetros
/// de afinación de infraestructura, no datos de negocio por tenant.
/// </summary>
public sealed class PermissionCacheOptions
{
    /// <summary>
    /// Tiempo de vida de una entrada cacheada (tanto L1 en memoria como L2/Redis si está configurado),
    /// aplicado tanto a los permisos efectivos por usuario como a los permisos por rol. Es el límite
    /// superior de "cuánto puede tardar en verse reflejado" un cambio de permisos que no pasó por
    /// <see cref="IPermissionCacheInvalidator"/> (por ejemplo, un cambio hecho directamente contra la
    /// base de datos, o la invalidación de una entrada L1 de OTRA instancia del proceso en un despliegue
    /// multi-instancia -- ver el comentario de <see cref="CachedPermissionService"/>). Un valor corto
    /// prioriza corrección (menos ventana de permiso potencialmente obsoleto) sobre reducción de carga en
    /// SQL Server; un valor largo hace lo contrario. Default: 60 segundos.
    /// </summary>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromSeconds(60);
}
