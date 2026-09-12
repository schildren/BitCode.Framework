using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
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

    /// <summary>
    /// Igual que <see cref="AddPermissionAsync{TRole}(RoleManager{TRole}, TRole, string, IPermissionCacheInvalidator, CancellationToken)"/>
    /// (invalida cache), pero además emite una entrada de auditoría (<see cref="IAuditWriter.WriteAsync"/>)
    /// con la acción <c>"roles.permission.grant"</c> -- cierre del pendiente explícito de F2-15/F2-D:
    /// "cambios de permisos/roles de F2-07/F2-09 son candidatas obvias a emitir auditoría" (ver
    /// <c>docs/guia-auditoria-inmutable.md</c>). El resultado se audita tanto si Identity concede el
    /// permiso (<see cref="AuditOutcome.Success"/>) como si falla (<see cref="AuditOutcome.Error"/>,
    /// <c>Reason</c> con la descripción concatenada de <see cref="IdentityResult.Errors"/>) -- un intento
    /// fallido de modificar permisos es igual de relevante para auditoría que uno exitoso.
    /// </summary>
    public static async Task<IdentityResult> AddPermissionAsync<TRole>(
        this RoleManager<TRole> roleManager,
        TRole role,
        string permission,
        IPermissionCacheInvalidator invalidator,
        IAuditWriter auditWriter,
        AuditActor actor,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
        where TRole : ApplicationRole
    {
        var result = await roleManager.AddPermissionAsync(role, permission, invalidator, cancellationToken);

        await WriteRolePermissionAuditEntryAsync(
            auditWriter, actor, tenantId, "roles.permission.grant", role, permission, result, cancellationToken);

        return result;
    }

    /// <summary>
    /// Igual que <see cref="RemovePermissionAsync{TRole}(RoleManager{TRole}, TRole, string, IPermissionCacheInvalidator, CancellationToken)"/>
    /// (invalida cache), pero además emite una entrada de auditoría con la acción
    /// <c>"roles.permission.revoke"</c> -- ver la documentación del overload equivalente de
    /// <see cref="AddPermissionAsync{TRole}(RoleManager{TRole}, TRole, string, IPermissionCacheInvalidator, IAuditWriter, AuditActor, Guid?, CancellationToken)"/>.
    /// </summary>
    public static async Task<IdentityResult> RemovePermissionAsync<TRole>(
        this RoleManager<TRole> roleManager,
        TRole role,
        string permission,
        IPermissionCacheInvalidator invalidator,
        IAuditWriter auditWriter,
        AuditActor actor,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
        where TRole : ApplicationRole
    {
        var result = await roleManager.RemovePermissionAsync(role, permission, invalidator, cancellationToken);

        await WriteRolePermissionAuditEntryAsync(
            auditWriter, actor, tenantId, "roles.permission.revoke", role, permission, result, cancellationToken);

        return result;
    }

    private static async Task WriteRolePermissionAuditEntryAsync<TRole>(
        IAuditWriter auditWriter,
        AuditActor actor,
        Guid? tenantId,
        string action,
        TRole role,
        string permission,
        IdentityResult result,
        CancellationToken cancellationToken)
        where TRole : ApplicationRole
    {
        var outcome = result.Succeeded ? AuditOutcome.Success : AuditOutcome.Error;
        var reason = result.Succeeded
            ? null
            : string.Join("; ", result.Errors.Select(e => e.Description));

        var request = new AuditEntryRequest(
            actor: actor,
            tenantId: tenantId,
            action: action,
            resource: new AuditResource("roles", role.Id.ToString()),
            outcome: outcome,
            reason: reason,
            metadata: new Dictionary<string, string?> { ["permission"] = permission, ["roleName"] = role.Name });

        // Fallo de escritura descartado a propósito -- mismo criterio que
        // AuditingAuthorizationPolicyEvaluator: no bloquear la operación de negocio (alta/baja de permiso)
        // ya resuelta por que el almacenamiento de auditoría tuvo un problema transitorio.
        _ = await auditWriter.WriteAsync(request, cancellationToken);
    }
}
