using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// Implementación por defecto de <see cref="IAuthorizationPolicyEvaluator"/> (F2-08, ABAC). Combina
/// RBAC (<see cref="IPermissionEvaluator"/>, F2-07) como base obligatoria y las
/// <see cref="IAbacRule"/> registradas como restricciones adicionales, deny-overrides:
/// <list type="number">
/// <item><description>Si <paramref name="principal"/> (ver <see cref="EvaluateAsync"/>) no está
/// autenticado, deniega de inmediato (<see cref="AbacDecisionReasons.NotAuthenticated"/>) -- nunca se
/// llega a calcular permisos ni a evaluar ninguna regla.</description></item>
/// <item><description>Calcula el permiso RBAC requerido como <c>"{resource.Type}.{action}"</c> (misma
/// convención <c>docs/convenciones.md</c> que <c>[RequirePermission]</c>) y lo verifica contra
/// <see cref="IPermissionEvaluator.EvaluateAsync"/>. Sin ese permiso, deniega
/// (<see cref="AbacDecisionReasons.PermissionDenied"/>) sin evaluar ninguna regla ABAC -- una regla
/// ABAC nunca puede conceder lo que RBAC ya denegó.</description></item>
/// <item><description>Con el permiso RBAC concedido, ejecuta cada <see cref="IAbacRule"/> registrada
/// cuyo <see cref="IAbacRule.AppliesTo"/> acepte el (tipo de recurso, acción) de esta evaluación. Basta
/// que UNA regla aplicable devuelva <see cref="AbacRuleEffect.Deny"/> para que el resultado final sea
/// denegado (<see cref="AbacDecisionReasons.RuleDenied"/>) -- no se necesita unanimidad ni "vencer" a
/// las demás reglas.</description></item>
/// <item><description>Si RBAC concede y ninguna regla aplicable deniega (incluido el caso de no tener
/// ninguna regla aplicable), el resultado final es <see cref="AbacDecision.Allow"/>
/// (<see cref="AbacDecisionReasons.Granted"/>).</description></item>
/// </list>
/// </summary>
public sealed class AuthorizationPolicyEvaluator(
    IPermissionEvaluator permissionEvaluator,
    IEnumerable<IAbacRule> rules) : IAuthorizationPolicyEvaluator
{
    public async Task<AbacDecision> EvaluateAsync(
        ClaimsPrincipal principal,
        AbacResource resource,
        string action,
        AbacContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        if (principal.Identity?.IsAuthenticated != true)
        {
            return AbacDecision.Deny(AbacDecisionReasons.NotAuthenticated);
        }

        var requiredPermission = $"{resource.Type}.{action}";
        var effectivePermissions = await permissionEvaluator.EvaluateAsync(principal, cancellationToken);
        if (!effectivePermissions.HasPermission(requiredPermission))
        {
            return AbacDecision.Deny(AbacDecisionReasons.PermissionDenied(requiredPermission));
        }

        var subject = new AbacSubject(principal, effectivePermissions);
        var effectiveContext = context ?? AbacContext.Empty;

        foreach (var rule in rules)
        {
            if (!rule.AppliesTo(resource.Type, action))
            {
                continue;
            }

            var outcome = await rule.EvaluateAsync(subject, resource, action, effectiveContext, cancellationToken);
            if (outcome.Effect == AbacRuleEffect.Deny)
            {
                return AbacDecision.Deny(AbacDecisionReasons.RuleDenied(outcome.Reason));
            }
        }

        return AbacDecision.Allow(AbacDecisionReasons.Granted(requiredPermission));
    }
}
