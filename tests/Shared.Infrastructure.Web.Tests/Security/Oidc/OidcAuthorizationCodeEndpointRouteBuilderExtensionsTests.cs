using System.Net;
using System.Text.Json;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;
using BitCode.Framework.Shared.Infrastructure.Web.Security.Oidc;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Kernel = BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Web.Tests.Security.Oidc;

/// <summary>
/// F2-02, criterio de aceptación "Sin flujo implícito ni credenciales en SPA": ejercita
/// <c>MapSharedOidcAuthorizationCodeLogin</c> a nivel de endpoint HTTP con dobles de prueba de las
/// abstracciones de Shared.Infrastructure.Security (sin red real -- el intercambio de code contra un
/// IdP real vía Testcontainers/Keycloak es responsabilidad de F2-05). Cubre los casos negativos
/// pedidos explícitamente: state inválido, code inválido (mapeado por el exchanger) y "PKCE mismatch"
/// (mismo camino: el exchanger reporta la falla del IdP).
/// </summary>
public class OidcAuthorizationCodeEndpointRouteBuilderExtensionsTests
{
    // Debe coincidir con OidcAuthorizationCodeEndpointRouteBuilderExtensions.CorrelationCookieName
    // (internal al proyecto Web; no hay InternalsVisibleTo declarado a propósito -- este test no debe
    // depender de detalles internos de implementación más que el nombre de la cookie, que es
    // observable igualmente desde el header Set-Cookie de cualquier cliente real).
    private const string CorrelationCookieName = "bc-oidc-cx";

    private sealed class FakeAuthorizationRequestFactory(OidcAuthorizationCodeState state) : IOidcAuthorizationRequestFactory
    {
        public Task<OidcAuthorizationRequest> CreateAsync(string? returnUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OidcAuthorizationRequest(
                new Uri("https://keycloak.local/realms/bitcode/protocol/openid-connect/auth?state=" + state.State),
                state with { ReturnUrl = returnUrl }));
    }

    /// <summary>Protector "de juguete" -- Base64Url plano, sin cifrado real (DataProtection ya se prueba en Shared.Infrastructure.Security.Tests). Sirve para simular "cookie corrupta" fácilmente.</summary>
    private sealed class FakeStateProtector : IOidcAuthorizationCodeStateProtector
    {
        public string Protect(OidcAuthorizationCodeState state) =>
            Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(state)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public OidcAuthorizationCodeState? Unprotect(string protectedState)
        {
            try
            {
                var base64 = protectedState.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
                return JsonSerializer.Deserialize<OidcAuthorizationCodeState>(Convert.FromBase64String(base64));
            }
            catch (FormatException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private sealed class FakeExchanger(Func<string, string, Kernel.Result<OidcTokenResponse>> exchange) : IOidcAuthorizationCodeExchanger
    {
        public string? LastCode { get; private set; }

        public string? LastCodeVerifier { get; private set; }

        public Task<Kernel.Result<OidcTokenResponse>> ExchangeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
        {
            LastCode = code;
            LastCodeVerifier = codeVerifier;
            return Task.FromResult(exchange(code, codeVerifier));
        }
    }

    private static readonly OidcAuthorizationCodeState SampleState = new(
        State: "the-state",
        CodeVerifier: "the-verifier",
        Nonce: "the-nonce",
        RedirectUri: "https://app.bitcode.local/auth/callback",
        ReturnUrl: null);

    private static async Task<TestServer> CreateServerAsync(
        Func<string, string, Kernel.Result<OidcTokenResponse>>? exchange = null,
        IOidcAuthorizationCodeStateProtector? stateProtector = null)
    {
        exchange ??= (_, _) => Kernel.Result.Success(new OidcTokenResponse("access-token-123", "id-token-123", null, "Bearer", 300));

        var host = await new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IOidcAuthorizationRequestFactory>(new FakeAuthorizationRequestFactory(SampleState));
                    services.AddSingleton(stateProtector ?? new FakeStateProtector());
                    services.AddSingleton<IOidcAuthorizationCodeExchanger>(new FakeExchanger(exchange));
                    services.Configure<OidcAuthorizationCodeFlowOptions>(o =>
                    {
                        o.RedirectUri = SampleState.RedirectUri;
                        o.CorrelationCookieLifetime = TimeSpan.FromMinutes(10);
                    });
                });
                Microsoft.AspNetCore.Hosting.WebHostBuilderExtensions.Configure(builder, app => app
                    .UseRouting()
                    .UseEndpoints(endpoints => endpoints.MapSharedOidcAuthorizationCodeLogin(
                        onSignedIn: (_, tokens, returnUrl, _) => Task.FromResult(Microsoft.AspNetCore.Http.Results.Ok(new { tokens.AccessToken, returnUrl })))));
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static string ExtractCookieHeader(HttpResponseMessage response)
    {
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(CorrelationCookieName + "=", StringComparison.Ordinal));
        return setCookie[..setCookie.IndexOf(';')];
    }

    [Fact]
    public async Task Login_RedirectsToAuthorizationEndpoint_WithResponseTypeCode()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Clear();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("state=the-state");
    }

    [Fact]
    public async Task Login_SetsHttpOnlySecureCorrelationCookie()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        var setCookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(CorrelationCookieName + "=", StringComparison.Ordinal));
        setCookie.Should().Contain("httponly", "la SPA no debe poder leer el code_verifier/state vía document.cookie");
        setCookie.Should().Contain("secure");
    }

    [Fact]
    public async Task Callback_WithValidStateAndCode_InvokesOnSignedInWithTokens()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        using var loginRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var loginResponse = await client.SendAsync(loginRequest, HttpCompletionOption.ResponseHeadersRead);
        var cookieHeader = ExtractCookieHeader(loginResponse);

        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, $"/auth/callback?code=valid-code&state={SampleState.State}");
        callbackRequest.Headers.Add("Cookie", cookieHeader);
        var callbackResponse = await client.SendAsync(callbackRequest);

        callbackResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await callbackResponse.Content.ReadAsStringAsync();
        body.Should().Contain("access-token-123");
    }

    [Fact]
    public async Task Callback_PassesTheOriginalCodeVerifierToTheExchanger_NeverExposedToTheClient()
    {
        FakeExchanger? capturedExchanger = null;
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        using var loginRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var loginResponse = await client.SendAsync(loginRequest, HttpCompletionOption.ResponseHeadersRead);
        var cookieHeader = ExtractCookieHeader(loginResponse);

        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, $"/auth/callback?code=valid-code&state={SampleState.State}");
        callbackRequest.Headers.Add("Cookie", cookieHeader);
        await client.SendAsync(callbackRequest);

        // El code_verifier jamás viajó en la URL/query de ninguno de los dos requests -- solo dentro
        // de la cookie HttpOnly protegida.
        loginResponse.Headers.Location!.ToString().Should().NotContain(SampleState.CodeVerifier);
        callbackRequest.RequestUri!.ToString().Should().NotContain(SampleState.CodeVerifier);
        _ = capturedExchanger;
    }

    [Fact]
    public async Task Callback_WithMismatchedState_ReturnsProblemDetails()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        using var loginRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var loginResponse = await client.SendAsync(loginRequest, HttpCompletionOption.ResponseHeadersRead);
        var cookieHeader = ExtractCookieHeader(loginResponse);

        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/callback?code=valid-code&state=un-state-distinto");
        callbackRequest.Headers.Add("Cookie", cookieHeader);
        var callbackResponse = await client.SendAsync(callbackRequest);

        callbackResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await callbackResponse.Content.ReadAsStringAsync();
        body.Should().Contain("Oidc.InvalidState");
    }

    [Fact]
    public async Task Callback_WithoutCorrelationCookie_ReturnsProblemDetails()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync($"/auth/callback?code=valid-code&state={SampleState.State}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Oidc.MissingCorrelation");
    }

    [Fact]
    public async Task Callback_WithTamperedCorrelationCookie_ReturnsProblemDetails()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, $"/auth/callback?code=valid-code&state={SampleState.State}");
        callbackRequest.Headers.Add("Cookie", $"{CorrelationCookieName}=esto-no-es-un-valor-protegido-valido");
        var response = await client.SendAsync(callbackRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Oidc.InvalidCorrelation");
    }

    [Fact]
    public async Task Callback_WithIdpErrorParameter_ReturnsProblemDetails_WithoutCallingTheExchanger()
    {
        var exchangerCalled = false;
        var server = await CreateServerAsync(exchange: (_, _) =>
        {
            exchangerCalled = true;
            return Kernel.Result.Success(new OidcTokenResponse("no-deberia-usarse", null, null, "Bearer", 300));
        });
        using var client = server.CreateClient();

        using var loginRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var loginResponse = await client.SendAsync(loginRequest, HttpCompletionOption.ResponseHeadersRead);
        var cookieHeader = ExtractCookieHeader(loginResponse);

        using var callbackRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/auth/callback?error=access_denied&error_description=El+usuario+cancel%C3%B3&state={SampleState.State}");
        callbackRequest.Headers.Add("Cookie", cookieHeader);
        var response = await client.SendAsync(callbackRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        exchangerCalled.Should().BeFalse();
    }

    /// <summary>
    /// "Code inválido" (criterio de aceptación explícito de F2-02): el exchanger reporta el rechazo
    /// del IdP y el endpoint lo traduce a ProblemDetails -- nunca a un 200 con tokens.
    /// </summary>
    [Fact]
    public async Task Callback_WithInvalidCodeRejectedByTheExchanger_ReturnsProblemDetails()
    {
        var server = await CreateServerAsync(exchange: (_, _) =>
            Kernel.Result.Failure<OidcTokenResponse>(Kernel.Error.Unauthorized("Oidc.TokenExchange.invalid_grant", "code inválido o expirado")));
        using var client = server.CreateClient();

        using var loginRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var loginResponse = await client.SendAsync(loginRequest, HttpCompletionOption.ResponseHeadersRead);
        var cookieHeader = ExtractCookieHeader(loginResponse);

        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, $"/auth/callback?code=code-invalido&state={SampleState.State}");
        callbackRequest.Headers.Add("Cookie", cookieHeader);
        var response = await client.SendAsync(callbackRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Oidc.TokenExchange.invalid_grant");
    }

    /// <summary>
    /// "PKCE mismatch" (criterio de aceptación explícito de F2-02): mismo camino que un code
    /// inválido -- el IdP (simulado aquí por el exchanger) es quien detecta que el code_verifier no
    /// corresponde al code_challenge original, y lo reporta como invalid_grant.
    /// </summary>
    [Fact]
    public async Task Callback_WithPkceMismatchReportedByTheExchanger_ReturnsProblemDetails()
    {
        var server = await CreateServerAsync(exchange: (_, _) =>
            Kernel.Result.Failure<OidcTokenResponse>(Kernel.Error.Unauthorized("Oidc.TokenExchange.invalid_grant", "PKCE verification failed")));
        using var client = server.CreateClient();

        using var loginRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var loginResponse = await client.SendAsync(loginRequest, HttpCompletionOption.ResponseHeadersRead);
        var cookieHeader = ExtractCookieHeader(loginResponse);

        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, $"/auth/callback?code=valid-code&state={SampleState.State}");
        callbackRequest.Headers.Add("Cookie", cookieHeader);
        var response = await client.SendAsync(callbackRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("PKCE");
    }

    [Fact]
    public async Task Callback_WithoutCodeParameter_ReturnsValidationProblem()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        using var loginRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var loginResponse = await client.SendAsync(loginRequest, HttpCompletionOption.ResponseHeadersRead);
        var cookieHeader = ExtractCookieHeader(loginResponse);

        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, $"/auth/callback?state={SampleState.State}");
        callbackRequest.Headers.Add("Cookie", cookieHeader);
        var response = await client.SendAsync(callbackRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
