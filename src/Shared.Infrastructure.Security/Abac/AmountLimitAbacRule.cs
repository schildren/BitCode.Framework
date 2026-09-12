using System.Globalization;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// Regla ABAC incorporada (F2-08) que cubre "monto" de forma genérica y configurable, sin que el
/// proyecto consumidor escriba código: por cada <see cref="AbacAmountLimitRule"/> configurada
/// (<see cref="AbacOptions.AmountLimitRules"/>) cuyo <see cref="AbacAmountLimitRule.ResourceType"/>
/// matchee el recurso evaluado, exige que el valor de
/// <see cref="AbacAmountLimitRule.ResourceAttributeKey"/> en el recurso no supere el valor del claim
/// <see cref="AbacAmountLimitRule.ClaimType"/> del sujeto.
/// <para>
/// Misma política de "sin dato, no se restringe" que <see cref="AttributeScopeAbacRule"/>: si el
/// recurso no declara el atributo configurado, si su valor no tiene forma numérica, o si el sujeto no
/// tiene el claim de límite configurado (o no tiene forma numérica), esta regla no restringe para esa
/// combinación -- un sujeto sin límite configurado (por ejemplo, un rol sin tope de aprobación) no
/// queda bloqueado por una regla que no le aplica.
/// </para>
/// </summary>
public sealed class AmountLimitAbacRule(IOptions<AbacOptions> options) : IAbacRule
{
    public bool AppliesTo(string resourceType, string action) =>
        options.Value.AmountLimitRules.Any(rule => Matches(rule.ResourceType, resourceType));

    public Task<AbacRuleOutcome> EvaluateAsync(
        AbacSubject subject,
        AbacResource resource,
        string action,
        AbacContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var rule in options.Value.AmountLimitRules.Where(rule => Matches(rule.ResourceType, resource.Type)))
        {
            if (!resource.Attributes.TryGetValue(rule.ResourceAttributeKey, out var rawAmount) ||
                !TryToDecimal(rawAmount, out var amount))
            {
                continue;
            }

            var claimValue = subject.Principal.FindFirst(rule.ClaimType)?.Value;
            if (claimValue is null || !decimal.TryParse(claimValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var maxAmount))
            {
                continue;
            }

            if (amount > maxAmount)
            {
                return Task.FromResult(AbacRuleOutcome.Deny(
                    $"amount-limit:{rule.ResourceAttributeKey}={amount.ToString(CultureInfo.InvariantCulture)}-exceeds:{rule.ClaimType}={maxAmount.ToString(CultureInfo.InvariantCulture)}"));
            }
        }

        return Task.FromResult(AbacRuleOutcome.NotApplicable("amount-limit:no-restriction"));
    }

    private static bool TryToDecimal(object? rawValue, out decimal amount)
    {
        switch (rawValue)
        {
            case decimal decimalValue:
                amount = decimalValue;
                return true;
            case int intValue:
                amount = intValue;
                return true;
            case long longValue:
                amount = longValue;
                return true;
            case double doubleValue:
                amount = (decimal)doubleValue;
                return true;
            case string stringValue when decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed):
                amount = parsed;
                return true;
            default:
                amount = default;
                return false;
        }
    }

    private static bool Matches(string configuredResourceType, string resourceType) =>
        configuredResourceType == "*" || string.Equals(configuredResourceType, resourceType, StringComparison.Ordinal);
}
