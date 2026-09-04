using Microsoft.AspNetCore.Authorization;

namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

public class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
