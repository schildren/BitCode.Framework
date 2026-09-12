using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.FeatureFlags;

/// <summary>
/// Implementación por defecto (y única, a la fecha de F4-12) de <see cref="IFeatureFlagProvider"/>:
/// resuelve cada flag desde <see cref="FeatureFlagsOptions"/> vía <see cref="IOptionsMonitor{TOptions}"/>
/// -- nunca <c>IOptions&lt;FeatureFlagsOptions&gt;</c> (esa variante congela el valor leído en el momento
/// del primer <c>Value</c>, sin reflejar una recarga posterior de configuración). Leer siempre
/// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> es lo que permite que un <c>ConfigMap</c> montado
/// como archivo con <c>reloadOnChange: true</c> (comportamiento nativo del proveedor de configuración de
/// archivo de ASP.NET Core) cambie el resultado de <see cref="IsEnabled"/> sin reiniciar el proceso.
/// </summary>
public sealed class ConfigurationFeatureFlagProvider : IFeatureFlagProvider
{
    private readonly IOptionsMonitor<FeatureFlagsOptions> _optionsMonitor;

    public ConfigurationFeatureFlagProvider(IOptionsMonitor<FeatureFlagsOptions> optionsMonitor)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;
    }

    public bool IsEnabled(string flagName, bool defaultValue = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flagName);

        var flags = _optionsMonitor.CurrentValue;
        return flags.TryGetValue(flagName, out var value) ? value : defaultValue;
    }
}
