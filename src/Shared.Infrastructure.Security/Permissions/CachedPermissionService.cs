using BitCode.Framework.Shared.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Decorador de <see cref="IPermissionService"/> (F2-09, cache de permisos): evita que
/// <see cref="IPermissionEvaluator"/> consulte SQL Server (vía <c>UserManager</c>/<c>RoleManager</c>, F1)
/// en cada request normal, cacheando el resultado de ambos métodos del <see cref="IPermissionService"/>
/// decorado -- nunca decora <see cref="IPermissionEvaluator"/> directamente: la única parte costosa de la
/// cadena es la consulta a SQL Server de <see cref="IPermissionService"/>, no la combinación de fuentes
/// (claims del token, narrowing por scope, validación de tenant) que ya hace
/// <see cref="PermissionEvaluator"/> sobre datos en memoria.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dos claves de cache, dos mecanismos, por diseño:</b>
/// </para>
/// <list type="bullet">
/// <item><description><see cref="GetPermissionsForUserAsync"/> usa <see cref="ITenantAwareCache"/>
/// (F1-16) -- los permisos de un usuario SÍ son datos sensibles a tenant (regla dura #14,
/// <c>docs/convenciones.md</c>): un <c>ApplicationUser</c> pertenece a un tenant concreto, y la
/// composición del <c>TenantId</c> en la clave evita que dos tenants (en el escenario, hoy inexistente,
/// de que compartieran el mismo <c>Guid</c> de usuario) puedan leer la entrada cacheada del otro.</description></item>
/// <item><description><see cref="GetPermissionsForRoleAsync"/> usa <see cref="HybridCache"/>
/// directamente, SIN el prefijo de tenant -- <c>ApplicationRole</c> todavía no implementa
/// <c>ITenantEntity</c> (gap documentado en <c>docs/guia-rbac-2.md</c>, sección "Qué NO resuelve F2-07"):
/// un rol creado en un tenant ya es visible/asignable en cualquier otro tenant del mismo despliegue, así
/// que sus permisos NO son un dato sensible a tenant hoy. Cachear por tenant igual sería inocuo (cada
/// tenant tendría su propia copia idéntica) pero desperdiciaría memoria/Redis sin ganar nada; si F2-07
/// alguna vez agrega tenancy a los roles (requiere aprobación humana, Plan Maestro sección 13), esta
/// decisión debe revisarse junto con esa migración.</description></item>
/// </list>
/// <para>
/// <b>Límite conocido de correctitud en despliegues multi-instancia:</b> <see cref="HybridCache"/> no
/// propaga la invalidación de la capa L1 (memoria) entre instancias del proceso -- <c>RemoveAsync</c>
/// (invocado por <see cref="InvalidateUserAsync"/>/<see cref="InvalidateRoleAsync"/>) borra la entrada L2
/// (Redis, si está configurado) y la copia L1 de la instancia que lo invoca, pero la copia L1 de OTRA
/// instancia sigue vigente hasta que expire por <see cref="PermissionCacheOptions.Expiration"/> (mismo
/// límite ya documentado para <see cref="TenantAwareCache"/>, F1-16: "la escritura a Redis L2 es
/// asíncrona, no asumir consistencia inmediata entre instancias"). Esto es lo que fija el techo real de
/// staleness de un permiso revocado en un despliegue de más de una instancia: nunca "indefinido" (el
/// techo es <see cref="PermissionCacheOptions.Expiration"/>), pero tampoco "inmediato en todas las
/// instancias" -- documentado explícitamente en <c>docs/guia-rbac-2.md</c> para que un proyecto que
/// necesite un techo más bajo ajuste <see cref="PermissionCacheOptions.Expiration"/> en consecuencia
/// (fail-closed acotado, nunca prometer más consistencia de la que el mecanismo realmente da).
/// </para>
/// <para>
/// <b>Corolario del mismo límite (verificado con Redis real, ver
/// <c>Integration/CachedPermissionServiceRedisIntegrationTests.cs</c>):</b> si
/// <see cref="InvalidateUserAsync"/>/<see cref="InvalidateRoleAsync"/> se invoca inmediatamente después
/// de la primera lectura que pobló la entrada (sin que la escritura asíncrona a la capa L2 haya
/// completado todavía), esa escritura diferida puede completarse DESPUÉS del borrado y dejar la entrada
/// vieja en Redis de todos modos -- mismo motivo por el que <c>TenantAwareCache</c> (F1-16) advierte "no
/// asumir consistencia inmediata entre instancias". En la práctica esto no suele ser un problema real:
/// entre que un permiso se cachea (una evaluación de autorización cualquiera) y que alguien lo cambie
/// deliberadamente (una acción administrativa separada) suele mediar tiempo de sobra para que la
/// escritura a Redis ya haya completado. Igual queda acotado por
/// <see cref="PermissionCacheOptions.Expiration"/> en el peor caso.
/// </para>
/// </remarks>
public sealed class CachedPermissionService(
    IPermissionService inner,
    ITenantAwareCache tenantAwareCache,
    HybridCache hybridCache,
    IOptions<PermissionCacheOptions> options) : IPermissionService, IPermissionCacheInvalidator
{
    private const string UserKeyPrefix = "permissions:user:";
    private const string RoleKeyPrefix = "permissions:role:";

    public async Task<IReadOnlyList<string>> GetPermissionsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var cached = await tenantAwareCache.GetOrCreateAsync(
            UserCacheKey(userId),
            async token => (await inner.GetPermissionsForUserAsync(userId, token)).ToArray(),
            BuildEntryOptions(),
            cancellationToken: cancellationToken);

        return cached;
    }

    public async Task<IReadOnlyList<string>> GetPermissionsForRoleAsync(
        string roleName,
        CancellationToken cancellationToken = default)
    {
        var cached = await hybridCache.GetOrCreateAsync(
            RoleCacheKey(roleName),
            async token => (await inner.GetPermissionsForRoleAsync(roleName, token)).ToArray(),
            BuildEntryOptions(),
            cancellationToken: cancellationToken);

        return cached;
    }

    public ValueTask InvalidateUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        tenantAwareCache.RemoveAsync(UserCacheKey(userId), cancellationToken);

    public ValueTask InvalidateRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
        hybridCache.RemoveAsync(RoleCacheKey(roleName), cancellationToken);

    private static string UserCacheKey(Guid userId) => $"{UserKeyPrefix}{userId:D}";

    private static string RoleCacheKey(string roleName) => $"{RoleKeyPrefix}{roleName}";

    private HybridCacheEntryOptions BuildEntryOptions() => new()
    {
        Expiration = options.Value.Expiration,
        LocalCacheExpiration = options.Value.Expiration,
    };
}
