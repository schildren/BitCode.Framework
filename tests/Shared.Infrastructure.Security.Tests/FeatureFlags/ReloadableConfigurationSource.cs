using Microsoft.Extensions.Configuration;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.FeatureFlags;

/// <summary>
/// Fuente de configuración de prueba que dispara explícitamente su change token
/// (<see cref="ConfigurationProvider.OnReload"/>) al llamar <see cref="ReloadableConfigurationProvider.SetAndReload"/>
/// -- a diferencia de <c>AddInMemoryCollection</c> (<c>MemoryConfigurationProvider.Set</c> actualiza el
/// valor pero nunca dispara <c>OnReload</c>), necesario para simular en un test unitario, sin
/// Testcontainers ni un archivo físico real, el mismo comportamiento que un <c>ConfigMap</c> montado como
/// archivo con <c>reloadOnChange: true</c> production (F4-12): un cambio del valor subyacente que además
/// notifica a <c>IOptionsMonitor&lt;T&gt;.OnChange</c>.
/// </summary>
internal sealed class ReloadableConfigurationProvider : ConfigurationProvider
{
    public ReloadableConfigurationProvider(IDictionary<string, string?> initialData)
    {
        Data = new Dictionary<string, string?>(initialData, StringComparer.OrdinalIgnoreCase);
    }

    public void SetAndReload(IDictionary<string, string?> newData)
    {
        Data = new Dictionary<string, string?>(newData, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }
}

internal sealed class ReloadableConfigurationSource : IConfigurationSource
{
    private readonly IDictionary<string, string?> _initialData;

    public ReloadableConfigurationSource(IDictionary<string, string?> initialData)
    {
        _initialData = initialData;
    }

    public ReloadableConfigurationProvider? Provider { get; private set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        Provider = new ReloadableConfigurationProvider(_initialData);
        return Provider;
    }
}
