using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Registra <see cref="IAuditWriter"/> (F2-15, Épica F2-D).
/// </summary>
public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="InMemoryAuditWriter"/> como implementación por defecto de
    /// <see cref="IAuditWriter"/> (<c>TryAddSingleton</c> -- singleton porque el almacenamiento en memoria
    /// necesita sobrevivir al scope de un request individual; una implementación persistente real que
    /// dependa de un <c>DbContext</c> por scope debe registrarse explícitamente como <c>Scoped</c> por el
    /// proyecto consumidor). Llamar a este método NO impide que un proyecto consumidor reemplace la
    /// implementación: un <c>AddScoped&lt;IAuditWriter, TImplementacionPropia&gt;()</c> llamado DESPUÉS de
    /// <c>AddSharedAuditing</c> gana la resolución (último registro para el mismo tipo de servicio, mismo
    /// principio que <c>AddSharedPermissionEvaluation</c>/F2-07) sin necesitar <c>Replace</c> explícito.
    /// </summary>
    public static IServiceCollection AddSharedAuditing(this IServiceCollection services)
    {
        services.TryAddSingleton<IAuditWriter, InMemoryAuditWriter>();
        return services;
    }
}
