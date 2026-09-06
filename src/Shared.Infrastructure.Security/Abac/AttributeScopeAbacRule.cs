using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// Regla ABAC incorporada (F2-08) que cubre "empresa" y "sucursal" (y cualquier otro atributo de
/// alcance equivalente) de forma genérica y configurable, sin que el proyecto consumidor escriba
/// código: por cada <see cref="AbacScopeAttributeRule"/> configurada (<see cref="AbacOptions.ScopeRules"/>)
/// cuyo <see cref="AbacScopeAttributeRule.ResourceType"/> matchee el recurso evaluado, exige que el
/// valor de <see cref="AbacScopeAttributeRule.ResourceAttributeKey"/> en el recurso figure entre los
/// valores del claim <see cref="AbacScopeAttributeRule.ClaimType"/> del sujeto.
/// <para>
/// Política deliberada de "sin dato, no se restringe" (mismo criterio que la validación de tenancy de
/// <c>PermissionEvaluator</c>, F2-07 -- la ausencia de un dato no bloquea, solo un valor presente y en
/// conflicto lo hace): si el recurso no declara el atributo configurado, o si el sujeto no tiene NINGÚN
/// valor para el claim configurado, esta regla no restringe para esa combinación (dos motivos
/// distintos: el primero -- el proyecto todavía no le pasó ese atributo al `AbacResource`, no hay nada
/// que comparar; el segundo -- un sujeto sin ese claim configurado, por ejemplo, un rol de servicio o
/// administrador sin alcance limitado, no se ve restringido por una regla de alcance que no le aplica).
/// Un sujeto CON al menos un valor de claim configurado, cuyo recurso declara el atributo pero el valor
/// no está entre esos claims, SÍ es denegado -- ese es el caso positivo de la regla.
/// </para>
/// </summary>
public sealed class AttributeScopeAbacRule(IOptions<AbacOptions> options) : IAbacRule
{
    public bool AppliesTo(string resourceType, string action) =>
        options.Value.ScopeRules.Any(rule => Matches(rule.ResourceType, resourceType));

    public Task<AbacRuleOutcome> EvaluateAsync(
        AbacSubject subject,
        AbacResource resource,
        string action,
        AbacContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var rule in options.Value.ScopeRules.Where(rule => Matches(rule.ResourceType, resource.Type)))
        {
            if (!resource.Attributes.TryGetValue(rule.ResourceAttributeKey, out var rawValue) || rawValue is null)
            {
                continue;
            }

            var allowedValues = subject.GetClaimValues(rule.ClaimType);
            if (allowedValues.Count == 0)
            {
                continue;
            }

            var resourceValue = Convert.ToString(rawValue, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            if (!allowedValues.Contains(resourceValue, StringComparer.Ordinal))
            {
                return Task.FromResult(AbacRuleOutcome.Deny(
                    $"scope:{rule.ResourceAttributeKey}={resourceValue}-not-in:{rule.ClaimType}"));
            }
        }

        return Task.FromResult(AbacRuleOutcome.NotApplicable("scope:no-restriction"));
    }

    private static bool Matches(string configuredResourceType, string resourceType) =>
        configuredResourceType == "*" || string.Equals(configuredResourceType, resourceType, StringComparison.Ordinal);
}
