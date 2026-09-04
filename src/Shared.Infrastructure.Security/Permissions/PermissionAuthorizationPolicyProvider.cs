using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Genera una AuthorizationPolicy por cada permiso bajo demanda (política = nombre del permiso),
/// sin necesidad de pre-registrar una policy por cada permiso del sistema con AddPolicy. Cualquier
/// nombre de policy que no reconozca cae al proveedor por defecto (policies "normales" registradas
/// explícitamente siguen funcionando igual).
/// </summary>
public class PermissionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackProvider = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallbackProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallbackProvider.GetFallbackPolicyAsync();

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Una policy "normal" ya registrada explícitamente (AddPolicy) tiene prioridad; solo se
        // sintetiza una PermissionRequirement cuando el nombre no coincide con ninguna conocida.
        var explicitPolicy = await _fallbackProvider.GetPolicyAsync(policyName);
        if (explicitPolicy is not null)
        {
            return explicitPolicy;
        }

        return new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
    }
}
