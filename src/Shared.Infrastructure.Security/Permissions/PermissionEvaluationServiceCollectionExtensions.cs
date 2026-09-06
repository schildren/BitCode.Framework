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
    /// <summary>
    /// Detección de F2-09 (fix post-revisión de arquitectura): reporta si
    /// <c>AddSharedPermissionCache</c> ya decoró <see cref="IPermissionService"/> con
    /// <c>CachedPermissionService</c> -- es la MISMA condición que
    /// <c>PermissionCacheServiceCollectionExtensions.AddSharedPermissionCache</c> usa para su propia
    /// idempotencia (un único registro "real", con <see cref="ServiceDescriptor.ImplementationFactory"/>,
    /// de <see cref="IPermissionCacheInvalidator"/> -- <see cref="NullPermissionCacheInvalidator"/> se
    /// registra con <c>TryAddScoped&lt;TService, TImplementation&gt;</c>, que fija
    /// <see cref="ServiceDescriptor.ImplementationType"/> en vez de una factory). Se centraliza acá (y no
    /// se duplica el mismo `Any` inline en dos archivos) porque <c>AddSharedSecurity</c> necesita la misma
    /// comprobación para detectar el orden de llamada inválido descrito en
    /// <c>PermissionCacheServiceCollectionExtensions</c>.
    /// </summary>
    internal static bool IsPermissionCacheAlreadyApplied(this IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(IPermissionCacheInvalidator) && d.ImplementationFactory is not null);

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

        // F2-09: sin llamar a AddSharedPermissionCache, no hay ningún cache que invalidar -- pero un
        // proyecto puede inyectar IPermissionCacheInvalidator igual (no-op) sin condicionar su código a
        // si el cache está habilitado, mismo patrón que NullPermissionService/NullTenantProvider.
        services.TryAddScoped<IPermissionCacheInvalidator, NullPermissionCacheInvalidator>();

        // TryAdd (no Add) a propósito -- F2-08 (ABAC) también llama a este método desde
        // AddSharedAbacAuthorization para garantizar IPermissionEvaluator disponible aunque el
        // consumidor solo haya llamado a ese método; en un proyecto que combina RBAC + ABAC (el caso
        // normal) también se llama desde AddSharedSecurity/AddSharedOidcAuthentication. Con Add
        // (comportamiento anterior a F2-08) esa doble llamada duplicaba el registro de
        // IAuthorizationHandler: PermissionAuthorizationHandler.HandleRequirementAsync se ejecutaba dos
        // veces por cada verificación de permiso (dos consultas redundantes vía IPermissionEvaluator),
        // sin cambiar el resultado pero desperdiciando una consulta a SQL Server por request. TryAdd
        // mantiene el mismo comportamiento para el caso de una sola llamada (sigue siendo el primer y
        // único registro) y lo hace correcto para el caso de dos llamadas.
        services.TryAddScoped<IPermissionEvaluator, PermissionEvaluator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, PermissionAuthorizationHandler>());
        services.TryAddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        services.AddAuthorization();

        return services;
    }
}
