using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;

/// <summary>
/// Registra las políticas de operaciones privilegiadas de F2-10 (step-up authentication y segregación
/// de funciones): dos <see cref="IAbacRule"/> adicionales (<see cref="StepUpAbacRule"/>,
/// <see cref="SegregationOfDutiesAbacRule"/>) que se suman a las incorporadas por F2-08 sobre el MISMO
/// <see cref="IAuthorizationPolicyEvaluator"/> -- ningún pipeline de autorización paralelo, ninguna
/// reescritura de <see cref="AuthorizationPolicyEvaluator"/>.
/// </summary>
public static class PrivilegedOperationsServiceCollectionExtensions
{
    /// <summary>
    /// Debe llamarse DESPUÉS de <c>AddSharedAbacAuthorization</c> (F2-08) -- necesita que
    /// <see cref="IAuthorizationPolicyEvaluator"/> ya esté registrado para que las dos reglas de esta
    /// tarea se ejecuten dentro de esa misma evaluación combinada (RBAC + ABAC + privilegiadas), en vez
    /// de crear un pipeline de autorización paralelo que un llamador podría olvidarse de invocar. Lanza
    /// <see cref="InvalidOperationException"/> en el arranque (nunca en tiempo de request) si no
    /// encuentra ese registro previo, mismo patrón que <c>PermissionCacheServiceCollectionExtensions.AddSharedPermissionCache</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>AddSharedAbacAuthorization</c> todavía no fue llamado sobre <paramref name="services"/>.
    /// </exception>
    public static IServiceCollection AddSharedPrivilegedOperationsPolicies(
        this IServiceCollection services,
        Action<PrivilegedOperationsOptions>? configureOptions = null)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IAuthorizationPolicyEvaluator)))
        {
            throw new InvalidOperationException(
                "AddSharedPrivilegedOperationsPolicies debe llamarse después de AddSharedAbacAuthorization " +
                "(F2-08) -- step-up y segregación de funciones (F2-10) se implementan como IAbacRule " +
                "adicionales sobre el mismo IAuthorizationPolicyEvaluator, no como un pipeline de " +
                "autorización paralelo. Llamá primero a services.AddSharedAbacAuthorization(...).");
        }

        services.AddOptions<PrivilegedOperationsOptions>().ValidateOnStart();
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        // TryAddEnumerable: un StepUpRequirement mal configurado (sin ningún criterio de evidencia) debe
        // fallar en el arranque (ValidateOnStart, arriba), no en la primera evaluación real -- ver
        // PrivilegedOperationsOptionsValidator. Idempotente frente a llamadas repetidas de este método.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PrivilegedOperationsOptions>, PrivilegedOperationsOptionsValidator>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAbacRule, StepUpAbacRule>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAbacRule, SegregationOfDutiesAbacRule>());

        return services;
    }
}
