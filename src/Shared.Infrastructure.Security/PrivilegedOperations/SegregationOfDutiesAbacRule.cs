using System.Globalization;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;

/// <summary>
/// Regla ABAC incorporada (F2-10) que implementa segregación de funciones (SoD) de dos formas
/// complementarias, ambas configurables sin que el proyecto consumidor escriba código:
/// <list type="number">
/// <item><description><b>Pares mutuamente excluyentes</b> (<see cref="PrivilegedOperationsOptions.MutuallyExclusivePermissions"/>):
/// un sujeto con ambos permisos de un par configurado a la vez entre sus
/// <see cref="EffectivePermissions"/> queda denegado en TODA evaluación (no depende del
/// recurso ni de la acción concretos -- es una restricción de identidad, verificada primero, antes de
/// evaluar ninguna regla maker-checker).</description></item>
/// <item><description><b>Maker-checker</b> (<see cref="PrivilegedOperationsOptions.MakerCheckerRules"/>):
/// para cada <see cref="MakerCheckerRule"/> cuyo (tipo de recurso, acción) matchee la evaluación actual,
/// exige que el actor identificado en <see cref="MakerCheckerRule.ActorResourceAttributeKey"/> del
/// recurso (quien ejecutó la acción anterior, por ejemplo, quien creó el pedido) sea DISTINTO del sujeto
/// actual (identificado por <see cref="MakerCheckerRule.SubjectClaimType"/>) -- el patrón clásico "quien
/// aprueba no puede ser quien creó".</description></item>
/// </list>
/// <para>
/// Política deliberadamente fail-closed en ambos casos, DISTINTA de la política "sin dato, no se
/// restringe" de las reglas ABAC genéricas de F2-08: para una regla maker-checker configurada y
/// aplicable, la ausencia del atributo de actor en el recurso o del claim de sujeto configurado NO se
/// interpreta como "no hay nada que comparar, no se restringe" -- se interpreta como "no se puede probar
/// que no hay conflicto de funciones", y se deniega. Ver <c>docs/guia-abac.md</c>, sección "Segregación
/// de funciones", para el detalle completo y ejemplos.
/// </para>
/// </summary>
public sealed class SegregationOfDutiesAbacRule(IOptions<PrivilegedOperationsOptions> options) : IAbacRule
{
    public bool AppliesTo(string resourceType, string action) =>
        options.Value.MutuallyExclusivePermissions.Count > 0 ||
        options.Value.MakerCheckerRules.Any(rule => Matches(rule, resourceType, action));

    public Task<AbacRuleOutcome> EvaluateAsync(
        AbacSubject subject,
        AbacResource resource,
        string action,
        AbacContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var pair in options.Value.MutuallyExclusivePermissions)
        {
            if (subject.EffectivePermissions.HasPermission(pair.PermissionA) &&
                subject.EffectivePermissions.HasPermission(pair.PermissionB))
            {
                return Task.FromResult(AbacRuleOutcome.Deny(
                    $"sod:mutually-exclusive-permissions:{pair.PermissionA}+{pair.PermissionB}"));
            }
        }

        var matchedMakerChecker = false;
        foreach (var rule in options.Value.MakerCheckerRules.Where(r => Matches(r, resource.Type, action)))
        {
            matchedMakerChecker = true;

            if (!resource.Attributes.TryGetValue(rule.ActorResourceAttributeKey, out var rawActor) || rawActor is null)
            {
                return Task.FromResult(AbacRuleOutcome.Deny(
                    $"sod:missing-actor-attribute:{rule.ActorResourceAttributeKey}"));
            }

            var subjectValues = subject.GetClaimValues(rule.SubjectClaimType);
            if (subjectValues.Count == 0)
            {
                return Task.FromResult(AbacRuleOutcome.Deny(
                    $"sod:missing-subject-claim:{rule.SubjectClaimType}"));
            }

            // OrdinalIgnoreCase (no Ordinal): actorValue proviene de un object? arbitrario del recurso
            // (típicamente un Guid o string que puso el handler de negocio) y subjectValues proviene de
            // claims emitidos por el IdP -- la MISMA identidad puede representarse con distinto casing
            // (por ejemplo, un Guid en mayúsculas vs. minúsculas) sin que eso sea, en absoluto, una
            // identidad distinta. Comparar con Ordinal fallaría en detectar el mismo actor y la regla
            // devolvería "sod:verified" en el escenario exacto que existe para prevenir -- violaría la
            // política fail-closed declarada en la cabecera de esta clase.
            var actorValue = Convert.ToString(rawActor, CultureInfo.InvariantCulture) ?? string.Empty;
            if (subjectValues.Contains(actorValue, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(AbacRuleOutcome.Deny(
                    $"sod:same-actor:{rule.ActorResourceAttributeKey}={actorValue}"));
            }
        }

        var hadAnythingToCheck = matchedMakerChecker || options.Value.MutuallyExclusivePermissions.Count > 0;
        return Task.FromResult(AbacRuleOutcome.NotApplicable(hadAnythingToCheck ? "sod:verified" : "sod:no-restriction"));
    }

    private static bool Matches(MakerCheckerRule rule, string resourceType, string action) =>
        (rule.ResourceType == "*" || string.Equals(rule.ResourceType, resourceType, StringComparison.Ordinal)) &&
        (rule.Action == "*" || string.Equals(rule.Action, action, StringComparison.Ordinal));
}
