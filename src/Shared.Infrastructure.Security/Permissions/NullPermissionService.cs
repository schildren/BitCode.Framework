namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Implementación por defecto de <see cref="IPermissionService"/> (F2-07, RBAC 2.0) para un proyecto
/// que registra <c>AddSharedOidcAuthentication</c> (F2-01) sin <c>AddSharedSecurity</c> — es decir,
/// sin Identity/roles locales. Nunca aporta permisos por sí sola: <see cref="IPermissionEvaluator"/>
/// sigue evaluando los permisos declarados directamente en el token del IdP externo (claim
/// <see cref="PermissionClaimTypes.Permission"/>) y el scope OAuth2 con forma de permiso
/// (<see cref="ScopeClaimTypes"/>), así que un proyecto solo-OIDC no queda sin ningún mecanismo de
/// autorización por permiso. Registrada con <c>TryAddScoped</c>: un proyecto que además llama a
/// <c>AddSharedSecurity</c> la reemplaza por <see cref="PermissionService{TUser,TRole}"/> (Identity
/// real), igual que <c>NullTenantProvider</c>/<c>NullIdempotencyKeyProvider</c> con sus respectivas
/// implementaciones productivas.
/// </summary>
public sealed class NullPermissionService : IPermissionService
{
    public Task<IReadOnlyList<string>> GetPermissionsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<string>> GetPermissionsForRoleAsync(
        string roleName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
