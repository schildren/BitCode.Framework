namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Valores posibles de <see cref="PermissionGrant.Source"/> (F2-07, RBAC 2.0) — documentan de dónde
/// vino cada permiso efectivo calculado por <see cref="IPermissionEvaluator"/>.
/// </summary>
public static class PermissionGrantSources
{
    /// <summary>
    /// El permiso proviene de expandir los roles asignados a un <c>ApplicationUser</c> local
    /// (Identity, F1) vía <see cref="IPermissionService.GetPermissionsForUserAsync"/>. La expansión
    /// agrega permisos de varios roles sin distinguir cuál concedió cada uno (comportamiento heredado
    /// de F1); usar <see cref="FromRoleClaim"/> para el caso donde sí se distingue por rol.
    /// </summary>
    public const string LocalIdentityRoles = "identity-local:roles";

    /// <summary>
    /// El permiso llegó como claim directo del token (<see cref="PermissionClaimTypes.Permission"/>),
    /// típicamente emitido por un IdP externo (F2-01) sin pasar por Identity local.
    /// </summary>
    public const string TokenPermissionClaim = "token:permission-claim";

    private const string RoleClaimPrefix = "token:role:";

    /// <summary>
    /// El permiso proviene de expandir un nombre de rol presente como claim del token
    /// (<see cref="System.Security.Claims.ClaimTypes.Role"/>) contra los roles conocidos localmente
    /// (<see cref="IPermissionService.GetPermissionsForRoleAsync"/>) — camino usado cuando la
    /// identidad no tiene un <c>ApplicationUser</c> local asociado (identidad puramente externa,
    /// F2-01).
    /// </summary>
    public static string FromRoleClaim(string roleName) => RoleClaimPrefix + roleName;
}
