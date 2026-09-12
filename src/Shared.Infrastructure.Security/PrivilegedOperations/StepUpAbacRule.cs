using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;

/// <summary>
/// Regla ABAC incorporada (F2-10) que implementa step-up authentication: para cada
/// <see cref="StepUpRequirement"/> configurada (<see cref="PrivilegedOperationsOptions.StepUpRequirements"/>)
/// cuyo (<see cref="StepUpRequirement.ResourceType"/>, <see cref="StepUpRequirement.Action"/>) matchee la
/// evaluación actual, exige evidencia de una autenticación reciente o reforzada -- método de
/// autenticación (claim <c>amr</c>) y/o antigüedad máxima (claim <c>auth_time</c>) -- ADEMÁS del permiso
/// RBAC/ABAC que <see cref="AuthorizationPolicyEvaluator"/> ya evaluó antes de ejecutar esta regla.
/// <para>
/// Política deliberadamente fail-closed, DISTINTA de la política "sin dato, no se restringe" de las
/// reglas ABAC genéricas de F2-08 (<see cref="AttributeScopeAbacRule"/>/<see cref="AmountLimitAbacRule"/>):
/// un requisito de step-up existe precisamente porque la operación es crítica, así que la ausencia del
/// claim de evidencia (el sujeto nunca se reautenticó, o el IdP no emite ese claim) deniega, no se
/// ignora. Ver <c>docs/guia-abac.md</c>, sección "Step-up authentication", para el detalle completo.
/// </para>
/// </summary>
public sealed class StepUpAbacRule(IOptions<PrivilegedOperationsOptions> options) : IAbacRule
{
    public bool AppliesTo(string resourceType, string action) =>
        options.Value.StepUpRequirements.Any(requirement => Matches(requirement, resourceType, action));

    public Task<AbacRuleOutcome> EvaluateAsync(
        AbacSubject subject,
        AbacResource resource,
        string action,
        AbacContext context,
        CancellationToken cancellationToken = default)
    {
        var matched = false;

        foreach (var requirement in options.Value.StepUpRequirements.Where(r => Matches(r, resource.Type, action)))
        {
            matched = true;

            var outcome = EvaluateRequirement(requirement, subject);
            if (outcome is not null)
            {
                return Task.FromResult(outcome);
            }
        }

        return Task.FromResult(matched
            ? AbacRuleOutcome.NotApplicable("step-up:verified")
            : AbacRuleOutcome.NotApplicable("step-up:not-required"));
    }

    /// <summary>
    /// Evalúa un único requisito ya sabido aplicable. Devuelve <see langword="null"/> si el sujeto
    /// cumple toda la evidencia exigida por este requisito puntual (ninguna denegación); devuelve un
    /// <see cref="AbacRuleOutcome"/> de <see cref="AbacRuleEffect.Deny"/> apenas falta o no alcanza
    /// alguno de los dos criterios configurados (método de autenticación, antigüedad).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// El requisito no configuró ningún criterio de evidencia (<see cref="StepUpRequirement.AcceptableAuthenticationMethods"/>
    /// ni <see cref="StepUpRequirement.MaxAuthenticationAge"/>). En un pipeline correctamente configurado
    /// (registrado vía <c>AddSharedPrivilegedOperationsPolicies</c>), esta condición ya la detecta y
    /// rechaza <see cref="PrivilegedOperationsOptionsValidator"/> en el arranque (<c>ValidateOnStart</c>)
    /// -- este throw NUNCA debería alcanzarse en tiempo de request. Se deja como defensa en profundidad
    /// (por ejemplo, si alguien construye <see cref="StepUpAbacRule"/> a mano, fuera de DI, con opciones
    /// no validadas) y no como el mecanismo primario de detección del error de configuración.
    /// </exception>
    private static AbacRuleOutcome? EvaluateRequirement(StepUpRequirement requirement, AbacSubject subject)
    {
        var hasMethodCriterion = requirement.AcceptableAuthenticationMethods is { Count: > 0 };
        var hasAgeCriterion = requirement.MaxAuthenticationAge is not null;

        if (!hasMethodCriterion && !hasAgeCriterion)
        {
            throw new InvalidOperationException(
                $"StepUpRequirement para '{requirement.ResourceType}.{requirement.Action}' no define " +
                "ningún criterio de evidencia (AcceptableAuthenticationMethods o MaxAuthenticationAge). " +
                "Un requisito de step-up sin evidencia exigida nunca podría denegar -- corregí la " +
                "configuración de PrivilegedOperationsOptions.StepUpRequirements.");
        }

        if (hasMethodCriterion)
        {
            var subjectMethods = subject.GetClaimValues(requirement.AuthenticationMethodClaimType);
            if (subjectMethods.Count == 0 ||
                !subjectMethods.Intersect(requirement.AcceptableAuthenticationMethods!, StringComparer.Ordinal).Any())
            {
                return AbacRuleOutcome.Deny(
                    $"step-up:missing-authentication-method:{requirement.AuthenticationMethodClaimType}");
            }
        }

        if (hasAgeCriterion)
        {
            var authTimeClaim = subject.Principal.FindFirst(requirement.AuthenticationTimeClaimType)?.Value;
            if (authTimeClaim is null || !long.TryParse(authTimeClaim, out var authTimeUnixSeconds))
            {
                return AbacRuleOutcome.Deny(
                    $"step-up:missing-authentication-time:{requirement.AuthenticationTimeClaimType}");
            }

            var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(authTimeUnixSeconds);
            var age = DateTimeOffset.UtcNow - authenticatedAt;
            if (age < TimeSpan.Zero || age > requirement.MaxAuthenticationAge!.Value)
            {
                return AbacRuleOutcome.Deny(
                    $"step-up:authentication-too-old:{requirement.AuthenticationTimeClaimType}");
            }
        }

        return null;
    }

    private static bool Matches(StepUpRequirement requirement, string resourceType, string action) =>
        (requirement.ResourceType == "*" || string.Equals(requirement.ResourceType, resourceType, StringComparison.Ordinal)) &&
        (requirement.Action == "*" || string.Equals(requirement.Action, action, StringComparison.Ordinal));
}
