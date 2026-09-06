using BitCode.Framework.Shared.Infrastructure.Security.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Secrets;

/// <summary>
/// F2-12: <see cref="ConfigurationSecretProvider"/> es el proveedor de desarrollo/local, sin
/// dependencia de infraestructura externa -- estos tests verifican su contrato contra
/// <see cref="IConfiguration"/> pura (nunca contra un secreto real, ver el criterio de aceptación
/// "Cero secretos en repositorio").
/// </summary>
public class ConfigurationSecretProviderTests
{
    private static ConfigurationSecretProvider CreateProvider(IConfiguration configuration, string? valuesSectionPath = null)
    {
        var options = Options.Create(new ConfigurationSecretProviderOptions
        {
            ValuesSectionPath = valuesSectionPath ?? "Secrets:Values",
        });
        return new ConfigurationSecretProvider(configuration, options);
    }

    [Fact]
    public async Task GetSecretAsync_WithExistingKey_ReturnsSuccessWithValue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Values:MiClave"] = "valor-de-prueba",
            })
            .Build();
        var provider = CreateProvider(configuration);

        var result = await provider.GetSecretAsync("MiClave");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("valor-de-prueba");
    }

    [Fact]
    public async Task GetSecretAsync_WithMissingKey_ReturnsFailureNotFound()
    {
        var configuration = new ConfigurationBuilder().Build();
        var provider = CreateProvider(configuration);

        var result = await provider.GetSecretAsync("NoExiste");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Secrets.NotFound");
    }

    [Fact]
    public async Task GetSecretAsync_WithEmptyKey_ReturnsFailureValidation()
    {
        var configuration = new ConfigurationBuilder().Build();
        var provider = CreateProvider(configuration);

        var result = await provider.GetSecretAsync(string.Empty);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Secrets.InvalidKey");
    }

    [Fact]
    public async Task GetSecretAsync_RespectsCustomValuesSectionPath()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MiApp:SecretosLocales:ClientSecretOidc"] = "otro-valor",
            })
            .Build();
        var provider = CreateProvider(configuration, "MiApp:SecretosLocales");

        var result = await provider.GetSecretAsync("ClientSecretOidc");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("otro-valor");
    }
}
