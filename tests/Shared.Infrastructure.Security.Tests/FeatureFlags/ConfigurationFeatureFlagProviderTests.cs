using BitCode.Framework.Shared.Infrastructure.Security.FeatureFlags;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.FeatureFlags;

public class ConfigurationFeatureFlagProviderTests
{
    [Fact]
    public void IsEnabled_FlagDeclaradoEnTrue_DevuelveTrue()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["FeatureFlags:NuevoFlujoDePagos"] = "true",
        });

        provider.IsEnabled("NuevoFlujoDePagos").Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_FlagDeclaradoEnFalse_DevuelveFalse()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["FeatureFlags:NuevoFlujoDePagos"] = "false",
        });

        provider.IsEnabled("NuevoFlujoDePagos", defaultValue: true).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_FlagNoDeclarado_DevuelveDefaultValue()
    {
        var provider = BuildProvider(new Dictionary<string, string?>());

        provider.IsEnabled("FlagInexistente", defaultValue: true).Should().BeTrue();
        provider.IsEnabled("FlagInexistente").Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_ComparacionDeNombreInsensibleAMayusculas()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["FeatureFlags:NuevoFlujoDePagos"] = "true",
        });

        provider.IsEnabled("nuevoflujodepagos").Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_NombreVacio_LanzaArgumentException()
    {
        var provider = BuildProvider(new Dictionary<string, string?>());

        var act = () => provider.IsEnabled(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsEnabled_TrasRecargarConfiguracion_ReflejaElValorNuevoSinReconstruirElProvider()
    {
        // ReloadableConfigurationSource (no AddInMemoryCollection): MemoryConfigurationProvider.Set no
        // dispara el change token que IOptionsMonitor<T>.OnChange/CurrentValue necesitan -- ver el
        // comentario de ReloadableConfigurationSource para el detalle completo.
        var source = new ReloadableConfigurationSource(new Dictionary<string, string?>
        {
            ["FeatureFlags:NuevoFlujoDePagos"] = "false",
        });
        var configurationRoot = new ConfigurationBuilder().Add(source).Build();

        var services = new ServiceCollection();
        services.Configure<FeatureFlagsOptions>(configurationRoot.GetSection(FeatureFlagsOptions.SectionName));
        services.AddSingleton<IFeatureFlagProvider, ConfigurationFeatureFlagProvider>();
        var serviceProvider = services.BuildServiceProvider();

        var featureFlagProvider = serviceProvider.GetRequiredService<IFeatureFlagProvider>();
        featureFlagProvider.IsEnabled("NuevoFlujoDePagos").Should().BeFalse();

        source.Provider!.SetAndReload(new Dictionary<string, string?>
        {
            ["FeatureFlags:NuevoFlujoDePagos"] = "true",
        });

        featureFlagProvider.IsEnabled("NuevoFlujoDePagos").Should().BeTrue();
    }

    private static ConfigurationFeatureFlagProvider BuildProvider(Dictionary<string, string?> configurationValues)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configurationValues).Build();

        var services = new ServiceCollection();
        services.Configure<FeatureFlagsOptions>(configuration.GetSection(FeatureFlagsOptions.SectionName));
        var serviceProvider = services.BuildServiceProvider();

        return new ConfigurationFeatureFlagProvider(
            serviceProvider.GetRequiredService<IOptionsMonitor<FeatureFlagsOptions>>());
    }
}
