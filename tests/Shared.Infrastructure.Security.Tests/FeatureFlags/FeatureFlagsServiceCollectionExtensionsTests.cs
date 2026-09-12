using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.FeatureFlags;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.FeatureFlags;

public class FeatureFlagsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharedFeatureFlags_RegistraConfigurationFeatureFlagProviderPorDefecto()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();

        services.AddSharedFeatureFlags(configuration);
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFeatureFlagProvider>().Should().BeOfType<ConfigurationFeatureFlagProvider>();
    }

    [Fact]
    public void AddSharedFeatureFlags_RegistraIAuditWriterSinLlamarAddSharedAuditingAparte()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();

        services.AddSharedFeatureFlags(configuration);
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAuditWriter>().Should().NotBeNull();
    }

    [Fact]
    public void AddSharedFeatureFlags_RegistraElHostedServiceDeAuditoriaDeCambios()
    {
        var services = new ServiceCollection();
        // FeatureFlagChangeAuditingService inyecta ILogger<T> -- un proyecto consumidor real siempre tiene
        // logging registrado (Serilog vía UseSharedSerilog u otro), así que este test lo agrega
        // explícitamente para no depender del comportamiento de un ServiceCollection "pelado".
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();

        services.AddSharedFeatureFlags(configuration);
        var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        hostedServices.OfType<FeatureFlagChangeAuditingService>().Should().ContainSingle();
    }

    [Fact]
    public void AddSharedFeatureFlags_LeeElFlagDesdeLaSeccionFeatureFlags()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["FeatureFlags:NuevoFlujoDePagos"] = "true",
            }).Build();

        services.AddSharedFeatureFlags(configuration);
        var provider = services.BuildServiceProvider();

        var featureFlagProvider = provider.GetRequiredService<IFeatureFlagProvider>();
        featureFlagProvider.IsEnabled("NuevoFlujoDePagos").Should().BeTrue();
    }
}
