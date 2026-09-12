using System.Net;
using System.Text;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc.AuthorizationCode;

/// <summary>
/// F2-02: intercambio de <c>code</c> por tokens contra el <c>token_endpoint</c>, incluyendo los casos
/// de error explícitamente pedidos por el criterio de aceptación ("code inválido", "PKCE mismatch") --
/// ambos los reporta el IdP como <c>invalid_grant</c> (RFC 6749 sección 5.2 / RFC 7636 sección 4.6),
/// así que se prueban con la misma forma de respuesta.
/// </summary>
public class OidcAuthorizationCodeExchangerTests
{
    private static readonly OidcDiscoveryDocument DiscoveryDocument = new(
        "https://keycloak.local/realms/bitcode/protocol/openid-connect/auth",
        "https://keycloak.local/realms/bitcode/protocol/openid-connect/token");

    private static (OidcAuthorizationCodeExchanger Sut, FakeHttpMessageHandler Handler) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHttpMessageHandler(respond);
        var httpClient = new HttpClient(handler);

        var discoveryProvider = Substitute.For<IOidcDiscoveryDocumentProvider>();
        discoveryProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(DiscoveryDocument);

        var oidcOptions = Options.Create(new OidcOptions
        {
            Authority = "https://keycloak.local/realms/bitcode",
            Audience = "bitcode-api",
            ClientId = "bitcode-spa",
        });

        return (new OidcAuthorizationCodeExchanger(httpClient, oidcOptions, discoveryProvider), handler);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ExchangeAsync_WithSuccessfulResponse_ReturnsTokens()
    {
        var (sut, _) = CreateSut(_ => JsonResponse(HttpStatusCode.OK, """
            {"access_token":"at-123","id_token":"idt-123","refresh_token":"rt-123","token_type":"Bearer","expires_in":300}
            """));

        var result = await sut.ExchangeAsync("valid-code", "valid-verifier", "https://app.bitcode.local/auth/callback");

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("at-123");
        result.Value.IdToken.Should().Be("idt-123");
        result.Value.RefreshToken.Should().Be("rt-123");
        result.Value.TokenType.Should().Be("Bearer");
        result.Value.ExpiresInSeconds.Should().Be(300);
    }

    [Fact]
    public async Task ExchangeAsync_PostsGrantTypeAuthorizationCodeAndCodeVerifier_WithoutClientSecret()
    {
        var (sut, handler) = CreateSut(_ => JsonResponse(HttpStatusCode.OK, """{"access_token":"at-123"}"""));

        await sut.ExchangeAsync("the-code", "the-verifier", "https://app.bitcode.local/auth/callback");

        handler.LastRequest!.RequestUri!.ToString().Should().Be(DiscoveryDocument.TokenEndpoint);
        var body = handler.LastRequestBody!;
        body.Should().Contain("grant_type=authorization_code");
        body.Should().Contain("code=the-code");
        body.Should().Contain("code_verifier=the-verifier");
        body.Should().NotContain(
            "client_secret",
            "cliente público protegido por PKCE: nunca se envía una credencial confidencial");
    }

    [Fact]
    public async Task ExchangeAsync_WithInvalidCode_ReturnsUnauthorizedFailure()
    {
        var (sut, _) = CreateSut(_ => JsonResponse(HttpStatusCode.BadRequest, """
            {"error":"invalid_grant","error_description":"El code ya fue usado o expiró"}
            """));

        var result = await sut.ExchangeAsync("code-invalido", "cualquier-verifier", "https://app.bitcode.local/auth/callback");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be("Oidc.TokenExchange.invalid_grant");
        result.Error.Description.Should().Contain("expiró");
    }

    [Fact]
    public async Task ExchangeAsync_WithPkceMismatch_ReturnsUnauthorizedFailure()
    {
        // RFC 7636 sección 4.6: el IdP recalcula S256(code_verifier) y lo compara contra el
        // code_challenge original de la request de autorización -- si no matchea, responde
        // invalid_grant, exactamente como un code inválido.
        var (sut, _) = CreateSut(_ => JsonResponse(HttpStatusCode.BadRequest, """
            {"error":"invalid_grant","error_description":"PKCE verification failed"}
            """));

        var result = await sut.ExchangeAsync("code-valido", "verifier-que-no-corresponde", "https://app.bitcode.local/auth/callback");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Oidc.TokenExchange.invalid_grant");
        result.Error.Description.Should().Contain("PKCE");
    }

    [Fact]
    public async Task ExchangeAsync_WithSuccessStatusButNoAccessToken_ReturnsFailure()
    {
        var (sut, _) = CreateSut(_ => JsonResponse(HttpStatusCode.OK, "{}"));

        var result = await sut.ExchangeAsync("code", "verifier", "https://app.bitcode.local/auth/callback");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Oidc.TokenExchange.InvalidResponse");
    }
}
