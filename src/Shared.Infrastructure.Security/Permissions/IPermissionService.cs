namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

public interface IPermissionService
{
    Task<IReadOnlyList<string>> GetPermissionsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
