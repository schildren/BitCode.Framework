namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// Vocabulario de <see cref="AbacDecision.Reason"/> que produce <see cref="AuthorizationPolicyEvaluator"/>
/// (F2-08) -- mismo propósito que <c>PermissionGrantSources</c> de F2-07: documentar de forma estable
/// (para logging/auditoría) por qué una decisión de autorización combinada (RBAC + ABAC) resultó como
/// resultó.
/// </summary>
public static class AbacDecisionReasons
{
    /// <summary>El principal de la evaluación no está autenticado -- default deny, ni siquiera se
    /// calculan permisos efectivos.</summary>
    public const string NotAuthenticated = "rbac:not-authenticated";

    private const string PermissionDeniedPrefix = "rbac:permission-denied:";

    /// <summary>El sujeto no tiene el permiso RBAC base requerido (<c>"{resource.Type}.{action}"</c>)
    /// entre sus permisos efectivos (<see cref="Permissions.IPermissionEvaluator"/>, F2-07) -- ninguna
    /// regla ABAC llega a evaluarse.</summary>
    public static string PermissionDenied(string requiredPermission) => PermissionDeniedPrefix + requiredPermission;

    private const string RuleDeniedPrefix = "abac:rule-denied:";

    /// <summary>RBAC concedió el permiso base, pero al menos una <see cref="IAbacRule"/> aplicable
    /// denegó (deny-overrides) -- <paramref name="ruleReason"/> es el <c>AbacRuleOutcome.Reason</c> de
    /// la regla concreta que denegó.</summary>
    public static string RuleDenied(string ruleReason) => RuleDeniedPrefix + ruleReason;

    private const string GrantedPrefix = "abac:granted:";

    /// <summary>RBAC concedió el permiso base y ninguna regla ABAC aplicable denegó (incluido el caso
    /// de no tener ninguna regla aplicable).</summary>
    public static string Granted(string requiredPermission) => GrantedPrefix + requiredPermission;
}
