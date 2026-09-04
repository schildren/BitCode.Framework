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
}
