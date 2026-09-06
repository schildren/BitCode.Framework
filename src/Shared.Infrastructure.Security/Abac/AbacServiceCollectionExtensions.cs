using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// Registra la infraestructura de autorización combinada RBAC + ABAC (F2-08):
/// <see cref="IAuthorizationPolicyEvaluator"/> y las dos <see cref="IAbacRule"/> genéricas incorporadas
/// (<see cref="AttributeScopeAbacRule"/>, <see cref="AmountLimitAbacRule"/>), configurables vía
/// <see cref="AbacOptions"/>.
/// </summary>
public static class AbacServiceCollectionExtensions
{
    /// <summary>
    /// Un proyecto llama a este método DESPUÉS de <c>AddSharedSecurity</c>/
    /// <c>AddSharedOidcAuthentication</c> (ambos ya registran RBAC, F2-07) -- pero también funciona
    /// llamado solo, sin ninguno de los dos: <c>AddSharedPermissionEvaluation</c> (F2-07) es idempotente
    /// desde el bugfix de esta misma tarea (ver <c>PermissionEvaluationServiceCollectionExtensions</c>),
    /// así que ABAC siempre tiene un <see cref="IPermissionEvaluator"/> disponible como la pieza RBAC
    /// del criterio combinado, sin duplicar su registro ni su costo en tiempo de ejecución cuando el
    /// proyecto también registró RBAC por su cuenta.
    /// </summary>
    public static IServiceCollection AddSharedAbacAuthorization(
        this IServiceCollection services,
        Action<AbacOptions>? configureOptions = null)
    {
        services.AddSharedPermissionEvaluation();

        services.AddOptions<AbacOptions>();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAbacRule, AttributeScopeAbacRule>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAbacRule, AmountLimitAbacRule>());
        services.AddScoped<IAuthorizationPolicyEvaluator, AuthorizationPolicyEvaluator>();

        return services;
    }
}
