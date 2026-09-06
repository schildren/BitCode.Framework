using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using Microsoft.AspNetCore.Identity;

namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

public static class RoleManagerPermissionExtensions
{
    public static async Task<IdentityResult> AddPermissionAsync<TRole>(
        this RoleManager<TRole> roleManager,
        TRole role,
        string permission)
        where TRole : ApplicationRole
    {
        var existingClaims = await roleManager.GetClaimsAsync(role);

        var alreadyGranted = existingClaims.Any(
            c => c.Type == PermissionClaimTypes.Permission && c.Value == permission);

        return alreadyGranted
            ? IdentityResult.Success
            : await roleManager.AddClaimAsync(role, new Claim(PermissionClaimTypes.Permission, permission));
    }

    /// <summary>
    /// (F2-09) Igual que <see cref="AddPermissionAsync{TRole}(RoleManager{TRole}, TRole, string)"/>, pero
    /// además invalida la entrada cacheada de permisos del rol (<see cref="IPermissionCacheInvalidator.InvalidateRoleAsync"/>)
    /// cuando la operación tuvo éxito -- un proyecto que registró
    /// <see cref="PermissionCacheServiceCollectionExtensions.AddSharedPermissionCache"/> debe usar este
    /// overload (o invalidar manualmente) para que el permiso agregado se refleje sin esperar el TTL del
    /// cache. Con <see cref="NullPermissionCacheInvalidator"/> (cache no habilitado), el llamado a
    /// invalidar es un no-op, así que este overload es seguro de usar siempre, esté o no habilitado el
    /// cache.
    /// </summary>
    public static async Task<IdentityResult> AddPermissionAsync<TRole>(
        this RoleManager<TRole> roleManager,
        TRole role,
        string permission,
        IPermissionCacheInvalidator invalidator,
        CancellationToken cancellationToken = default)
        where TRole : ApplicationRole
    {
        var result = await roleManager.AddPermissionAsync(role, permission);
        if (result.Succeeded)
        {
            await invalidator.InvalidateRoleAsync(role.Name!, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Quita un permiso previamente concedido a un rol (F2-09) -- simétrico de
    /// <see cref="AddPermissionAsync{TRole}(RoleManager{TRole}, TRole, string)"/>. Un rol sin ese permiso
    /// concedido devuelve <see cref="IdentityResult.Success"/> sin ninguna llamada a Identity, igual
    /// criterio que "agregar un permiso ya concedido" del método de alta.
    /// </summary>
    public static async Task<IdentityResult> RemovePermissionAsync<TRole>(
        this RoleManager<TRole> roleManager,
        TRole role,
        string permission)
        where TRole : ApplicationRole
    {
        var existingClaims = await roleManager.GetClaimsAsync(role);

        var claim = existingClaims.FirstOrDefault(
            c => c.Type == PermissionClaimTypes.Permission && c.Value == permission);

        return claim is null
            ? IdentityResult.Success
            : await roleManager.RemoveClaimAsync(role, claim);
    }

    /// <summary>
    /// (F2-09) Igual que <see cref="RemovePermissionAsync{TRole}(RoleManager{TRole}, TRole, string)"/>,
    /// invalidando además la entrada cacheada del rol cuando la operación tuvo éxito -- ver el overload
    /// equivalente de <see cref="AddPermissionAsync{TRole}(RoleManager{TRole}, TRole, string, IPermissionCacheInvalidator, CancellationToken)"/>.
    /// </summary>
    public static async Task<IdentityResult> RemovePermissionAsync<TRole>(
        this RoleManager<TRole> roleManager,
        TRole role,
        string permission,
        IPermissionCacheInvalidator invalidator,
        CancellationToken cancellationToken = default)
        where TRole : ApplicationRole
    {
        var result = await roleManager.RemovePermissionAsync(role, permission);
        if (result.Succeeded)
        {
            await invalidator.InvalidateRoleAsync(role.Name!, cancellationToken);
        }

        return result;
    }
}
