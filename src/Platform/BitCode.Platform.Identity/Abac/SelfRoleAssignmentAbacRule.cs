using BitCode.Framework.Shared.Infrastructure.Security.Abac;

namespace BitCode.Framework.Platform.Identity.Abac;

/// <summary>
/// Regla ABAC (F2-08) propia de Identity Administration: ni siquiera un actor con el permiso RBAC
/// <c>"identidad.usuarios.roles.asignar"</c> puede asignarse un rol a sí mismo -- separación de
/// funciones mínima sobre una operación sensible (escalada de privilegios). RBAC concede el permiso
/// base; esta regla solo puede restringir, nunca conceder (mismo contrato que
/// <c>AttributeScopeAbacRule</c>/<c>AmountLimitAbacRule</c>, F2-08) -- ver
/// <see cref="AsignarRolAUsuarioCommandHandler"/> y <c>docs/guia-identity-administration.md</c>, sección
/// "RBAC + ABAC en la asignación de roles".
/// </summary>
internal sealed class SelfRoleAssignmentAbacRule : IAbacRule
{
    public const string ResourceType = "identidad.usuarios.roles";
    public const string Action = "asignar";
    public const string TargetUserIdAttribute = "targetUserId";
    public const string ActorUserIdAttribute = "actorUserId";

    public bool AppliesTo(string resourceType, string action) =>
        string.Equals(resourceType, ResourceType, StringComparison.Ordinal)
        && string.Equals(action, Action, StringComparison.Ordinal);

    public Task<AbacRuleOutcome> EvaluateAsync(
        AbacSubject subject,
        AbacResource resource,
        string action,
        AbacContext context,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = resource.Attributes.TryGetValue(TargetUserIdAttribute, out var target)
            ? target?.ToString()
            : null;
        var actorUserId = resource.Attributes.TryGetValue(ActorUserIdAttribute, out var actor)
            ? actor?.ToString()
            : null;

        if (targetUserId is not null
            && actorUserId is not null
            && string.Equals(targetUserId, actorUserId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AbacRuleOutcome.Deny("identidad:no-autoasignacion-de-roles"));
        }

        return Task.FromResult(AbacRuleOutcome.NotApplicable("identidad:sin-restriccion"));
    }
}
