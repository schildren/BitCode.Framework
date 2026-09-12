using BitCode.Framework.Shared.Infrastructure.Security.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Secrets;

/// <summary>
/// F2-12: <see cref="SecretProviderServiceCollectionExtensions.AddSharedSecretProvider"/> debe
/// registrar el proveedor concreto exclusivamente a partir de la sección "Secrets:Provider" -- mismo
/// criterio de intercambiabilidad por configuración que F2-01 (ADR 0004) exige para el adapter OIDC.
/// </summary>
public class SecretProviderServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharedSecretProvider_WithoutConfiguration_DefaultsToConfigurationProvider()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddSharedSecretProvider(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISecretProvider>().Should().BeOfType<ConfigurationSecretProvider>();
    }

    [Fact]
    public void AddSharedSecretProvider_WithProviderConfiguration_RegistersConfigurationSecretProvider()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Secrets:Provider"] = "Configuration" })
            .Build();

        services.AddSharedSecretProvider(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISecretProvider>().Should().BeOfType<ConfigurationSecretProvider>();
    }

    [Fact]
    public void AddSharedSecretProvider_WithProviderVault_RegistersVaultSecretProvider()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Provider"] = "Vault",
                ["Secrets:Vault:Address"] = "https://vault.local:8200",
                ["Secrets:Vault:Token"] = "root-token",
            })
            .Build();

        services.AddSharedSecretProvider(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISecretProvider>().Should().BeOfType<VaultSecretProvider>();
    }

    [Fact]
    public void AddSharedSecretProvider_WithVaultProviderAndNoAddress_Throws()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Provider"] = "Vault",
                ["Secrets:Vault:Token"] = "root-token",
            })
            .Build();

        var act = () => services.AddSharedSecretProvider(configuration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedSecretProvider_WithVaultProviderAndNoToken_Throws()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Provider"] = "Vault",
                ["Secrets:Vault:Address"] = "https://vault.local:8200",
            })
            .Build();

        var act = () => services.AddSharedSecretProvider(configuration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedSecretProvider_WithVaultProviderAndHttpAddressWithoutAllowInsecureHttp_Throws()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Provider"] = "Vault",
                ["Secrets:Vault:Address"] = "http://vault.local:8200",
                ["Secrets:Vault:Token"] = "root-token",
            })
            .Build();

        var act = () => services.AddSharedSecretProvider(configuration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedSecretProvider_WithVaultProviderAndHttpAddressWithAllowInsecureHttp_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Provider"] = "Vault",
                ["Secrets:Vault:Address"] = "http://vault.local:8200",
                ["Secrets:Vault:Token"] = "root-token",
                ["Secrets:Vault:AllowInsecureHttp"] = "true",
            })
            .Build();

        var act = () => services.AddSharedSecretProvider(configuration);

        act.Should().NotThrow();
    }
}
