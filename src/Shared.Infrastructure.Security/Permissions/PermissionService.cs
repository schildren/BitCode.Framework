using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using Microsoft.AspNetCore.Identity;

namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Los permisos NO tienen tablas propias: se representan como Claims de tipo "permission" sobre
/// los roles de ASP.NET Core Identity (AspNetRoleClaims), reutilizando el modelo ya provisto por
/// Identity en vez de duplicarlo con un esquema Permission/RolePermission propio.
/// </summary>
public class PermissionService<TUser, TRole>(UserManager<TUser> userManager, RoleManager<TRole> roleManager)
    : IPermissionService
    where TUser : ApplicationUser
    where TRole : ApplicationRole
{
    public async Task<IReadOnlyList<string>> GetPermissionsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return [];
        }

        var roleNames = await userManager.GetRolesAsync(user);
        var permissions = new HashSet<string>();

        foreach (var roleName in roleNames)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            var claims = await roleManager.GetClaimsAsync(role);

            foreach (var claim in claims.Where(c => c.Type == PermissionClaimTypes.Permission))
            {
                permissions.Add(claim.Value);
            }
        }

        return permissions.ToList();
    }
}
