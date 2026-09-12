namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// El efecto que una <see cref="IAbacRule"/> concreta produjo al evaluarse (F2-08). Deliberadamente sin
/// un cuarto valor "conceder": una regla ABAC del framework nunca puede otorgar por sí sola un permiso
/// que RBAC (<see cref="IAuthorizationPolicyEvaluator"/>) ya haya denegado -- solo puede restringir
/// (<see cref="Deny"/>) un permiso que RBAC ya concedió, o no tener nada que decir
/// (<see cref="NotApplicable"/>). Ver <see cref="AuthorizationPolicyEvaluator"/> para el algoritmo de
/// combinación (deny-overrides).
/// </summary>
public enum AbacRuleEffect
{
    NotApplicable,
    Deny,
}

/// <summary>
/// Resultado de <see cref="IAbacRule.EvaluateAsync"/>: el <see cref="Effect"/> y un
/// <see cref="Reason"/> trazable (mismo espíritu que <c>PermissionGrant.Source</c> de F2-07) que
/// explica por qué -- para que <see cref="AbacDecision.Reason"/> permita reconstruir, ante una pregunta
/// de auditoría, exactamente qué regla bloqueó una operación y con qué valores concretos.
/// </summary>
public sealed record AbacRuleOutcome(AbacRuleEffect Effect, string Reason)
{
    public static AbacRuleOutcome NotApplicable(string reason) => new(AbacRuleEffect.NotApplicable, reason);

    public static AbacRuleOutcome Deny(string reason) => new(AbacRuleEffect.Deny, reason);
}
