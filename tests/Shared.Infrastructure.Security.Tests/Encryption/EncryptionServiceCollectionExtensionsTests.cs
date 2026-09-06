using BitCode.Framework.Shared.Infrastructure.Security.Encryption;
using BitCode.Framework.Shared.Infrastructure.Security.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Encryption;

/// <summary>
/// F2-13: <see cref="EncryptionServiceCollectionExtensions.AddSharedEncryption"/> registra
/// <see cref="IEncryptionProvider"/> apoyado en <see cref="ISecretProvider"/> ya registrado (F2-12) --
/// no un tipo concreto, mismo principio de intercambiabilidad que el resto del framework.
/// </summary>
public class EncryptionServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharedEncryption_RegistersAesGcmEncryptionProvider()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSharedSecretProvider(configuration);

        services.AddSharedEncryption(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IEncryptionProvider>().Should().BeOfType<AesGcmEncryptionProvider>();
    }

    [Fact]
    public void AddSharedEncryption_WithoutExplicitConfiguration_UsesDefaultOptions()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSharedSecretProvider(configuration);

        services.AddSharedEncryption(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<EncryptionOptions>>().Value;

        options.ActiveKeyVersionSecretKey.Should().Be("Encryption:ActiveKeyVersion");
        options.KeyMaterialSecretKeyPrefix.Should().Be("Encryption:Keys:");
    }

    [Fact]
    public void AddSharedEncryption_RespectsCustomConfiguredSecretKeys()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:ActiveKeyVersionSecretKey"] = "MiApp:ClaveActiva",
                ["Encryption:KeyMaterialSecretKeyPrefix"] = "MiApp:Claves:",
            })
            .Build();
        services.AddSharedSecretProvider(configuration);

        services.AddSharedEncryption(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<EncryptionOptions>>().Value;

        options.ActiveKeyVersionSecretKey.Should().Be("MiApp:ClaveActiva");
        options.KeyMaterialSecretKeyPrefix.Should().Be("MiApp:Claves:");
    }
}
