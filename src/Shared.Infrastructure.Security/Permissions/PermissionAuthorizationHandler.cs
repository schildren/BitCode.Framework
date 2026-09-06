using Microsoft.AspNetCore.Authorization;

namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Default deny: solo llama a <see cref="AuthorizationHandlerContext.Succeed"/> cuando
/// <see cref="IPermissionEvaluator"/> (F2-07, RBAC 2.0) confirma el permiso requerido entre los
/// permisos efectivos del principal actual — cualquier otro caso (sin autenticar, permiso ausente,
/// tenant inconsistente) deja el requirement sin cumplir. Antes de F2-07 este handler llamaba
/// directamente a <see cref="IPermissionService"/> (solo roles de Identity local, vía userId); ahora
/// delega en el evaluador normalizado para que también funcione con una identidad autenticada por un
/// IdP externo (F2-01) sin <c>ApplicationUser</c> local.
/// </summary>
public class PermissionAuthorizationHandler(IPermissionEvaluator permissionEvaluator)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var effectivePermissions = await permissionEvaluator.EvaluateAsync(context.User);

        if (effectivePermissions.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
