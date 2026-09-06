using System.Net;
using System.Text;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc.AuthorizationCode;

/// <summary>
/// F2-02 (apoyado en el descubrimiento estándar de F2-01): igual que
/// <see cref="OidcAuthenticationServiceCollectionExtensionsTests"/>, nunca se hardcodea un endpoint de
/// autorización/token propietario -- se resuelve siempre desde el documento de metadata OIDC.
/// </summary>
public class OidcDiscoveryDocumentProviderTests
{
    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (OidcDiscoveryDocumentProvider Sut, FakeHttpMessageHandler Handler) CreateSut(
        string? metadataAddress = null,
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var handler = new FakeHttpMessageHandler(respond ?? (_ => JsonResponse("""
            {
                "authorization_endpoint": "https://keycloak.local/realms/bitcode/protocol/openid-connect/auth",
                "token_endpoint": "https://keycloak.local/realms/bitcode/protocol/openid-connect/token"
            }
            """)));
        var httpClient = new HttpClient(handler);

        var oidcOptions = Options.Create(new OidcOptions
        {
            Authority = "https://keycloak.local/realms/bitcode",
            Audience = "bitcode-api",
            MetadataAddress = metadataAddress,
        });

        return (new OidcDiscoveryDocumentProvider(httpClient, oidcOptions, new OidcDiscoveryDocumentCache()), handler);
    }

    [Fact]
    public async Task GetAsync_WithoutMetadataAddressOverride_FetchesWellKnownDiscoveryDocument()
    {
        var (sut, handler) = CreateSut();

        var document = await sut.GetAsync();

        handler.LastRequest!.RequestUri!.ToString().Should().Be(
            "https://keycloak.local/realms/bitcode/.well-known/openid-configuration");
        document.AuthorizationEndpoint.Should().Be("https://keycloak.local/realms/bitcode/protocol/openid-connect/auth");
        document.TokenEndpoint.Should().Be("https://keycloak.local/realms/bitcode/protocol/openid-connect/token");
    }

    [Fact]
    public async Task GetAsync_WithMetadataAddressOverride_FetchesThatAddressInstead()
    {
        var (sut, handler) = CreateSut(metadataAddress: "https://keycloak.local/custom-discovery");

        await sut.GetAsync();

        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://keycloak.local/custom-discovery");
    }

    [Fact]
    public async Task GetAsync_CalledTwice_OnlyFetchesOnce_UsingTheCache()
    {
        var (sut, handler) = CreateSut();

        await sut.GetAsync();
        await sut.GetAsync();

        // Ambas llamadas comparten la misma OidcDiscoveryDocumentCache (mismo Sut) -- una sola
        // request HTTP real, la segunda se sirve desde memoria.
        handler.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_WithMissingEndpointsInPayload_ThrowsInvalidOperationException()
    {
        var (sut, _) = CreateSut(respond: _ => JsonResponse("{}"));

        var act = () => sut.GetAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
