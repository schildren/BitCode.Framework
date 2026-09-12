namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// Resultado de <see cref="IAuthorizationPolicyEvaluator.EvaluateAsync"/> (F2-08): la decisión binaria
/// (<see cref="Allowed"/>) junto con un <see cref="Reason"/> trazable -- mismo espíritu que
/// <c>PermissionGrant.Source</c> de F2-07: ante una pregunta de auditoría ("¿por qué se denegó/concedió
/// esta operación?") alcanza con inspeccionar <see cref="Reason"/>, sin reconstruir manualmente qué
/// paso de la evaluación combinada (RBAC o una regla ABAC concreta) decidió. Ver
/// <see cref="AbacDecisionReasons"/> para el vocabulario de razones que produce la implementación por
/// defecto.
/// </summary>
public sealed record AbacDecision(bool Allowed, string Reason)
{
    public static AbacDecision Allow(string reason) => new(true, reason);

    public static AbacDecision Deny(string reason) => new(false, reason);
}
