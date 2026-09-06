namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

public interface IPermissionService
{
    Task<IReadOnlyList<string>> GetPermissionsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permisos concedidos directamente a un rol por nombre (F2-07, RBAC 2.0), sin pasar por un
    /// usuario local de Identity — necesario para <see cref="IPermissionEvaluator"/> cuando la
    /// identidad autenticada viene de un IdP externo (F2-01) y trae nombres de rol como claim
    /// (<see cref="System.Security.Claims.ClaimTypes.Role"/>) pero no tiene un
    /// <see cref="Identity.ApplicationUser"/> local asociado. Un rol inexistente devuelve una lista
    /// vacía, igual que <see cref="GetPermissionsForUserAsync"/> con un usuario inexistente.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionsForRoleAsync(string roleName, CancellationToken cancellationToken = default);
}
