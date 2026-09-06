using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;

/// <summary>
/// Valida <see cref="PrivilegedOperationsOptions"/> en el arranque (<c>ValidateOnStart</c>), nunca en
/// tiempo de request -- mismo patrón que el resto del módulo (ver, por ejemplo, el
/// <see cref="InvalidOperationException"/> de <c>AddSharedPrivilegedOperationsPolicies</c> por orden de
/// registro inválido). Un <see cref="StepUpRequirement"/> sin ningún criterio de evidencia configurado
/// (ni <see cref="StepUpRequirement.AcceptableAuthenticationMethods"/> ni
/// <see cref="StepUpRequirement.MaxAuthenticationAge"/>) es un error de configuración (típicamente un
/// typo al declarar la operación crítica en <c>InfrastructureModule</c>): antes de esta validación, el
/// error solo se manifestaba dentro de <see cref="StepUpAbacRule.EvaluateAsync"/>, en la primera
/// solicitud real de un usuario contra esa operación -- un 500 genérico indistinguible de un fallo de
/// infraestructura. Ahora falla en <c>BuildServiceProvider()</c>/arranque, visible en CI/smoke tests.
/// </summary>
public sealed class PrivilegedOperationsOptionsValidator : IValidateOptions<PrivilegedOperationsOptions>
{
    public ValidateOptionsResult Validate(string? name, PrivilegedOperationsOptions options)
    {
        var errors = new List<string>();

        foreach (var requirement in options.StepUpRequirements)
        {
            var hasMethodCriterion = requirement.AcceptableAuthenticationMethods is { Count: > 0 };
            var hasAgeCriterion = requirement.MaxAuthenticationAge is not null;

            if (!hasMethodCriterion && !hasAgeCriterion)
            {
                errors.Add(
                    $"StepUpRequirement para '{requirement.ResourceType}.{requirement.Action}' no define " +
                    "ningún criterio de evidencia (AcceptableAuthenticationMethods o MaxAuthenticationAge). " +
                    "Un requisito de step-up sin evidencia exigida nunca podría denegar -- corregí la " +
                    "configuración de PrivilegedOperationsOptions.StepUpRequirements.");
            }
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
