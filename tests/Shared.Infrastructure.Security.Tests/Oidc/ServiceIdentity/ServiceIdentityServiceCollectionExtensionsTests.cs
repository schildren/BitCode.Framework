using BitCode.Framework.Shared.Infrastructure.Security.Oidc.ServiceIdentity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc.ServiceIdentity;

/// <summary>
/// F2-04: registro de la identidad de servicio -- validación de configuración obligatoria
/// ("Oidc:ServiceIdentity:Authority"/"ClientId"/uno de "ClientSecret"/"CertificateThumbprint") y que
/// <see cref="IServiceTokenProvider"/> queda resoluble por DI.
/// </summary>
public class ServiceIdentityServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void AddSharedServiceIdentity_WithValidConfiguration_RegistersIServiceTokenProvider()
    {
        var services = new ServiceCollection();
        services.AddSharedServiceIdentity(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Oidc:ServiceIdentity:Authority"] = "https://keycloak.local/realms/bitcode",
            ["Oidc:ServiceIdentity:ClientId"] = "bitcode-workload-a",
            ["Oidc:ServiceIdentity:ClientSecret"] = "s3cr3t",
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IServiceTokenProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddSharedServiceIdentity_WithoutConfigurationSection_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddSharedServiceIdentity(new ConfigurationBuilder().Build());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedServiceIdentity_WithoutAuthority_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddSharedServiceIdentity(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Oidc:ServiceIdentity:ClientId"] = "bitcode-workload-a",
            ["Oidc:ServiceIdentity:ClientSecret"] = "s3cr3t",
        }));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedServiceIdentity_WithoutClientId_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddSharedServiceIdentity(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Oidc:ServiceIdentity:Authority"] = "https://keycloak.local/realms/bitcode",
            ["Oidc:ServiceIdentity:ClientSecret"] = "s3cr3t",
        }));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedServiceIdentity_WithoutClientSecretOrCertificateThumbprint_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddSharedServiceIdentity(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Oidc:ServiceIdentity:Authority"] = "https://keycloak.local/realms/bitcode",
            ["Oidc:ServiceIdentity:ClientId"] = "bitcode-workload-a",
        }));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedServiceIdentity_WithCertificateThumbprintInsteadOfClientSecret_DoesNotThrow()
    {
        // El contrato acepta CertificateThumbprint como alternativa a ClientSecret (preparado para
        // workload identity federada), aunque el flujo concreto todavía no esté implementado -- el
        // registro no debe fallar solo por elegir esa opción del contrato.
        var services = new ServiceCollection();
        var act = () => services.AddSharedServiceIdentity(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Oidc:ServiceIdentity:Authority"] = "https://keycloak.local/realms/bitcode",
            ["Oidc:ServiceIdentity:ClientId"] = "bitcode-workload-a",
            ["Oidc:ServiceIdentity:CertificateThumbprint"] = "AA11BB22",
        }));

        act.Should().NotThrow();
    }

    [Fact]
    public void AddServiceIdentityAuthentication_AddsHandlerToTypedHttpClientPipeline()
    {
        var services = new ServiceCollection();
        services.AddSharedServiceIdentity(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Oidc:ServiceIdentity:Authority"] = "https://keycloak.local/realms/bitcode",
            ["Oidc:ServiceIdentity:ClientId"] = "bitcode-workload-a",
            ["Oidc:ServiceIdentity:ClientSecret"] = "s3cr3t",
        }));

        services.AddHttpClient<FakeDownstreamApiClient>().AddServiceIdentityAuthentication();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<FakeDownstreamApiClient>();

        client.Should().NotBeNull();
    }

    private sealed class FakeDownstreamApiClient
    {
        public FakeDownstreamApiClient(HttpClient httpClient)
        {
            HttpClient = httpClient;
        }

        public HttpClient HttpClient { get; }
    }
}
