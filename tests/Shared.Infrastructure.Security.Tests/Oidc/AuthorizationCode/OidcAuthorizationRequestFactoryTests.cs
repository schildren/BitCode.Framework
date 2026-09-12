using System.Web;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc.AuthorizationCode;

/// <summary>
/// F2-02, criterio de aceptación "Sin flujo implícito": estos tests verifican que la URL de
/// autorización construida por <see cref="OidcAuthorizationRequestFactory"/> nunca puede convertirse en
/// un flujo implícito (response_type siempre "code") y siempre incluye PKCE (S256).
/// </summary>
public class OidcAuthorizationRequestFactoryTests
{
    private static readonly OidcDiscoveryDocument DiscoveryDocument = new(
        "https://keycloak.local/realms/bitcode/protocol/openid-connect/auth",
        "https://keycloak.local/realms/bitcode/protocol/openid-connect/token");

    private static OidcAuthorizationRequestFactory CreateSut(
        string clientId = "bitcode-spa",
        string redirectUri = "https://app.bitcode.local/auth/callback")
    {
        var discoveryProvider = Substitute.For<IOidcDiscoveryDocumentProvider>();
        discoveryProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(DiscoveryDocument);

        var oidcOptions = Options.Create(new OidcOptions
        {
            Authority = "https://keycloak.local/realms/bitcode",
            Audience = "bitcode-api",
            ClientId = clientId,
        });

        var flowOptions = Options.Create(new OidcAuthorizationCodeFlowOptions
        {
            RedirectUri = redirectUri,
        });

        return new OidcAuthorizationRequestFactory(discoveryProvider, oidcOptions, flowOptions);
    }

    [Fact]
    public async Task CreateAsync_UsesResponseTypeCode_NeverImplicitFlow()
    {
        var sut = CreateSut();

        var request = await sut.CreateAsync(returnUrl: null);

        var query = HttpUtility.ParseQueryString(request.AuthorizationUri.Query);
        query["response_type"].Should().Be("code");
    }

    [Fact]
    public async Task CreateAsync_IncludesPkceS256Challenge()
    {
        var sut = CreateSut();

        var request = await sut.CreateAsync(returnUrl: null);

        var query = HttpUtility.ParseQueryString(request.AuthorizationUri.Query);
        query["code_challenge_method"].Should().Be("S256");
        query["code_challenge"].Should().Be(PkceGenerator.CreateCodeChallenge(request.State.CodeVerifier));
    }

    [Fact]
    public async Task CreateAsync_UsesConfiguredAuthorizationEndpointClientIdAndRedirectUri()
    {
        var sut = CreateSut(clientId: "otro-cliente", redirectUri: "https://otra-app.local/callback");

        var request = await sut.CreateAsync(returnUrl: null);

        request.AuthorizationUri.GetLeftPart(UriPartial.Path).Should().Be(DiscoveryDocument.AuthorizationEndpoint);
        var query = HttpUtility.ParseQueryString(request.AuthorizationUri.Query);
        query["client_id"].Should().Be("otro-cliente");
        query["redirect_uri"].Should().Be("https://otra-app.local/callback");
    }

    [Fact]
    public async Task CreateAsync_NeverIncludesAClientSecret()
    {
        var sut = CreateSut();

        var request = await sut.CreateAsync(returnUrl: null);

        request.AuthorizationUri.Query.Should().NotContain(
            "client_secret",
            "PKCE reemplaza la necesidad de un client_secret para un cliente público (SPA)");
    }

    [Fact]
    public async Task CreateAsync_GeneratesUniqueStateAndNonce()
    {
        var sut = CreateSut();

        var request = await sut.CreateAsync(returnUrl: null);

        var query = HttpUtility.ParseQueryString(request.AuthorizationUri.Query);
        query["state"].Should().Be(request.State.State);
        query["nonce"].Should().Be(request.State.Nonce);
        request.State.State.Should().NotBe(request.State.Nonce);
    }

    [Fact]
    public async Task CreateAsync_TwoCalls_ProduceDifferentStatesAndVerifiers()
    {
        var sut = CreateSut();

        var first = await sut.CreateAsync(returnUrl: null);
        var second = await sut.CreateAsync(returnUrl: null);

        first.State.State.Should().NotBe(second.State.State);
        first.State.CodeVerifier.Should().NotBe(second.State.CodeVerifier);
    }

    [Fact]
    public async Task CreateAsync_PropagatesReturnUrlIntoState()
    {
        var sut = CreateSut();

        var request = await sut.CreateAsync(returnUrl: "/dashboard");

        request.State.ReturnUrl.Should().Be("/dashboard");
    }

    [Fact]
    public async Task CreateAsync_WithoutClientId_Throws()
    {
        var sut = CreateSut(clientId: null!);

        var act = () => sut.CreateAsync(returnUrl: null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
