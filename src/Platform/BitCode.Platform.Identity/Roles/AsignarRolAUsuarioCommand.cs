using BitCode.Framework.Platform.Identity.Abac;
using BitCode.Framework.Platform.Identity.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace BitCode.Framework.Platform.Identity.Roles;

/// <summary>
/// Asigna un rol a un usuario -- la operación sensible de referencia del módulo (checklist Fase 6,
/// requisito común 7 "RBAC y ABAC"): el endpoint exige el permiso RBAC
/// <see cref="IdentityAdministrationPermissions.UsuariosRolesAsignar"/> (distinto del permiso de solo
/// lectura <see cref="IdentityAdministrationPermissions.UsuariosVer"/>) vía <c>[RequirePermission]</c>,
/// y el handler además evalúa explícitamente <see cref="IAuthorizationPolicyEvaluator"/> (F2-08) para
/// aplicar <see cref="SelfRoleAssignmentAbacRule"/> -- un actor no puede asignarse un rol a sí mismo
/// aunque tenga el permiso RBAC. Requiere que el proyecto consumidor haya registrado
/// <c>AddSharedAbacAuthorization</c> (F2-08) -- documentado en <c>docs/guia-identity-administration.md</c>.
/// </summary>
internal sealed record AsignarRolAUsuarioCommand(Guid UserId, string NombreRol) : ICommand, IIdempotentCommand;

internal sealed class AsignarRolAUsuarioCommandValidator : AbstractValidator<AsignarRolAUsuarioCommand>
{
    public AsignarRolAUsuarioCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.NombreRol).NotEmpty();
    }
}

internal sealed class AsignarRolAUsuarioCommandHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IAuthorizationPolicyEvaluator authorizationPolicyEvaluator,
    IPermissionCacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    IIdentityAdministrationActorContext actorContext)
    : IRequestHandler<AsignarRolAUsuarioCommand, Result>
{
    public async Task<Result> Handle(AsignarRolAUsuarioCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return Result.Failure(new Error(
                "Identidad.Usuarios.NoEncontrado", "El usuario indicado no existe.", ErrorType.NotFound));
        }

        var role = await roleManager.FindByNameAsync(request.NombreRol);
        if (role is null)
        {
            return Result.Failure(new Error(
                "Identidad.Roles.NoEncontrado", "El rol indicado no existe.", ErrorType.NotFound));
        }

        var principal = actorContext.GetCurrentPrincipal();
        if (principal is null)
        {
            // Fail-closed (mismo criterio que StepUpAbacRule/SegregationOfDutiesAbacRule, F2-10): sin
            // un ClaimsPrincipal resuelto no hay forma de evaluar la regla ABAC de no-autoasignación,
            // así que la operación se deniega en vez de omitir el control.
            return Result.Failure(new Error(
                "Identidad.Usuarios.Roles.ActorNoResuelto",
                "No se pudo resolver el actor autenticado para evaluar la autorización.",
                ErrorType.Forbidden));
        }

        var resource = new AbacResource(
            SelfRoleAssignmentAbacRule.ResourceType,
            new Dictionary<string, object?>
            {
                [SelfRoleAssignmentAbacRule.TargetUserIdAttribute] = user.Id.ToString(),
                [SelfRoleAssignmentAbacRule.ActorUserIdAttribute] =
                    actorContext.GetCurrentActor().Id,
            });

        var decision = await authorizationPolicyEvaluator.EvaluateAsync(
            principal, resource, SelfRoleAssignmentAbacRule.Action, cancellationToken: cancellationToken);

        if (!decision.Allowed)
        {
            await WriteAuditEntryAsync(user, role.Name, AuditOutcome.Denied, decision.Reason, cancellationToken);
            return Result.Failure(new Error(
                "Identidad.Usuarios.Roles.NoAutorizado", decision.Reason, ErrorType.Forbidden));
        }

        var identityResult = await userManager.AddToRoleAsync(user, request.NombreRol);
        if (identityResult.Succeeded)
        {
            await cacheInvalidator.InvalidateUserAsync(user.Id, cancellationToken);
        }

        await WriteAuditEntryAsync(
            user,
            role.Name,
            identityResult.Succeeded ? AuditOutcome.Success : AuditOutcome.Error,
            identityResult.Succeeded ? null : string.Join("; ", identityResult.Errors.Select(e => e.Description)),
            cancellationToken);

        return identityResult.Succeeded
            ? Result.Success()
            : Result.Failure(new Error(
                "Identidad.Usuarios.Roles.NoSePudoAsignar",
                string.Join("; ", identityResult.Errors.Select(e => e.Description)),
                ErrorType.Validation));
    }

    private async Task WriteAuditEntryAsync(
        ApplicationUser user, string? roleName, AuditOutcome outcome, string? reason, CancellationToken cancellationToken)
    {
        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: user.TenantId == Guid.Empty ? null : user.TenantId,
            action: "identidad.usuarios.roles.asignar",
            resource: new AuditResource("identidad.usuarios", user.Id.ToString()),
            outcome: outcome,
            reason: reason,
            metadata: new Dictionary<string, string?> { ["rol"] = roleName });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);
    }
}
