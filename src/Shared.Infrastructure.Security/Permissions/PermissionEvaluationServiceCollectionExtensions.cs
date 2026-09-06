using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Registra el evaluador de permisos efectivos (<see cref="IPermissionEvaluator"/>, F2-07, RBAC 2.0) y
/// la infraestructura de autorización dinámica por permiso
/// (<see cref="PermissionAuthorizationHandler"/>/<see cref="PermissionAuthorizationPolicyProvider"/>).
/// Compartida entre <c>SecurityServiceCollectionExtensions.AddSharedSecurity</c> (JWT propio) y
/// <c>OidcAuthenticationServiceCollectionExtensions.AddSharedOidcAuthentication</c> (F2-01) — antes de
/// F2-07 solo <c>AddSharedSecurity</c> la registraba, así que <c>[RequirePermission]</c> quedaba sin
/// ningún <see cref="IAuthorizationPolicyProvider"/> dinámico bajo autenticación puramente OIDC.
/// </summary>
public static class PermissionEvaluationServiceCollectionExtensions
{
    public static IServiceCollection AddSharedPermissionEvaluation(this IServiceCollection services)
    {
        // Mismo patrón de fallback que AddSharedPersistence (F1-12/F1-15): un proyecto de un único
        // tenant, o uno que todavía no llamó a AddSharedPersistence, sigue pudiendo resolver
        // ITenantProvider/ITenantContext para el evaluador.
        services.TryAddScoped<ITenantProvider, NullTenantProvider>();
        services.TryAddScoped<ITenantContext, TenantContext>();

        // Un proyecto solo-OIDC (AddSharedOidcAuthentication sin AddSharedSecurity) no tiene
        // Identity/roles locales: NullPermissionService deja que IPermissionEvaluator siga
        // funcionando igual con los permisos declarados directamente en el token (F2-07).
        services.TryAddScoped<IPermissionService, NullPermissionService>();

        services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.TryAddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        services.AddAuthorization();

        return services;
    }
}
