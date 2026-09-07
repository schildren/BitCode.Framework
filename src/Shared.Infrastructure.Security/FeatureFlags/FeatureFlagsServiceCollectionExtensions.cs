using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.FeatureFlags;

/// <summary>
/// Registra el mecanismo simple de feature flags de runtime (F4-12): <see cref="IFeatureFlagProvider"/>
/// (lectura on/off desde configuración externalizada) y <see cref="FeatureFlagChangeAuditingService"/>
/// (auditoría de cada transición de valor detectada al recargar configuración).
/// </summary>
public static class FeatureFlagsServiceCollectionExtensions
{
    /// <summary>
    /// Enlaza <see cref="FeatureFlagsOptions"/> a la sección <see cref="FeatureFlagsOptions.SectionName"/>
    /// de <paramref name="configuration"/> con <c>services.Configure&lt;FeatureFlagsOptions&gt;(...)</c> --
    /// a diferencia de un simple <c>services.AddOptions&lt;T&gt;().Bind(...)</c>, esta variante registra
    /// además el <c>ConfigurationChangeTokenSource&lt;FeatureFlagsOptions&gt;</c> que
    /// <see cref="IOptionsMonitor{TOptions}.OnChange"/> necesita para disparar una notificación cuando el
    /// proveedor de configuración subyacente recarga (por ejemplo, un archivo montado desde un
    /// <c>ConfigMap</c> con <c>reloadOnChange: true</c>) -- sin este registro, <see cref="ConfigurationFeatureFlagProvider"/>
    /// seguiría leyendo el valor correcto en cada llamada (<c>CurrentValue</c> igual refleja la config más
    /// reciente), pero <see cref="FeatureFlagChangeAuditingService"/> nunca se enteraría del cambio.
    /// <para>
    /// Registra también <see cref="IAuditWriter"/> vía <c>AddSharedAuditing()</c> (idempotente, mismo
    /// patrón que <c>PrivilegedOperationsServiceCollectionExtensions.AddSharedPrivilegedOperationsPolicies</c>)
    /// para que un proyecto consumidor no tenga que recordar el orden de llamada entre ambos métodos -- un
    /// <c>AddSharedAuditing()</c> propio, llamado antes o después de este método, sigue funcionando igual
    /// ("último registro gana" para <see cref="IAuditWriter"/>).
    /// </para>
    /// </summary>
    public static IServiceCollection AddSharedFeatureFlags(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSharedAuditing();

        services.Configure<FeatureFlagsOptions>(configuration.GetSection(FeatureFlagsOptions.SectionName));
        services.TryAddSingleton<IFeatureFlagProvider, ConfigurationFeatureFlagProvider>();
        services.AddSingleton<FeatureFlagChangeAuditingService>();
        services.AddHostedService(sp => sp.GetRequiredService<FeatureFlagChangeAuditingService>());

        return services;
    }
}
