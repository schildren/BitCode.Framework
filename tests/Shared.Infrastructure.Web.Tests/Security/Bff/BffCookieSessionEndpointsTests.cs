using System.Net;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc.Bff;
using BitCode.Framework.Shared.Infrastructure.Web.Security.Bff;
using BitCode.Framework.Shared.Infrastructure.Web.Security.Oidc;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BitCode.Framework.Shared.Infrastructure.Web.Tests.Security.Bff;

/// <summary>
/// F2-03, criterio de aceptación "Tokens no quedan expuestos al navegador": ejercita
/// <c>MapSharedOidcAuthorizationCodeLogin</c> (F2-02) con <see cref="BffOidcSignInHandler.HandleAsync"/>
/// como <c>onSignedIn</c> -- el escenario de uso real del BFF -- de punta a punta contra un
/// <see cref="TestServer"/>: login, un endpoint protegido que representa el proxy hacia una API (el
/// mismo mecanismo que usa <c>BffAccessTokenRequestTransform</c>, ver
/// <c>BffAccessTokenRequestTransformTests</c> para la prueba unitaria del transform de YARP en sí) y
/// logout.
/// </summary>
public class BffCookieSessionEndpointsTests
{
    private const string BffCookieName = "bc-bff-session";

    private sealed class FakeAuthorizationRequestFactory : IOidcAuthorizationRequestFactory
    {
        public Task<OidcAuthorizationRequest> CreateAsync(string? returnUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OidcAuthorizationRequest(
                new Uri("https://keycloak.local/realms/bitcode/protocol/openid-connect/auth?state=the-state"),
                new OidcAuthorizationCodeState("the-state", "the-verifier", "the-nonce", "https://bff.bitcode.local/auth/callback", returnUrl)));
    }

    private sealed class FakeExchanger(string accessToken, string? idToken) : IOidcAuthorizationCodeExchanger
    {
        public Task<Shared.Kernel.Result<OidcTokenResponse>> ExchangeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default) =>
            Task.FromResult(Shared.Kernel.Result.Success(new OidcTokenResponse(accessToken, idToken, "refresh-token-123", "Bearer", 300)));
    }

    private sealed class FakeDiscoveryDocumentProvider : IOidcDiscoveryDocumentProvider
    {
        public Task<OidcDiscoveryDocument> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OidcDiscoveryDocument(
                "https://keycloak.local/realms/bitcode/protocol/openid-connect/auth",
                "https://keycloak.local/realms/bitcode/protocol/openid-connect/token",
                EndSessionEndpoint: "https://keycloak.local/realms/bitcode/protocol/openid-connect/logout"));
    }

    /// <summary>id_token de juguete -- firma vacía a propósito: F2-03 decodifica el payload sin
    /// validar la firma (ver el comentario de <see cref="BffOidcSignInHandler"/>), así que alcanza con
    /// que el segundo segmento sea un JSON Base64Url válido.</summary>
    private static string BuildFakeIdToken(string subject, string name) =>
        "header." + Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { sub = subject, name })).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";

    /// <param name="idToken">
    /// <see langword="null"/> (default) genera un id_token de juguete válido; pasar <c>""</c>
    /// simula explícitamente que el IdP no devolvió id_token (caso negativo).
    /// </param>
    private static async Task<TestServer> CreateServerAsync(string accessToken = "access-token-123", string? idToken = null)
    {
        idToken = idToken is null ? BuildFakeIdToken("user-1", "Ada Lovelace") : idToken.Length == 0 ? null : idToken;

        var host = await new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthorization();
                    services.AddSingleton<IOidcAuthorizationRequestFactory>(new FakeAuthorizationRequestFactory());
                    services.AddSingleton<IOidcAuthorizationCodeExchanger>(new FakeExchanger(accessToken, idToken));
                    services.AddSingleton<IOidcDiscoveryDocumentProvider>(new FakeDiscoveryDocumentProvider());
                    services.AddDataProtection();
                    services.AddSingleton<IOidcAuthorizationCodeStateProtector, OidcAuthorizationCodeStateProtector>();
                    services.Configure<OidcOptions>(o =>
                    {
                        o.Authority = "https://keycloak.local/realms/bitcode";
                        o.Audience = "bitcode-api";
                        o.ClientId = "bitcode-spa";
                    });
                    services.Configure<OidcAuthorizationCodeFlowOptions>(o =>
                    {
                        o.RedirectUri = "https://bff.bitcode.local/auth/callback";
                    });

                    var configuration = new ConfigurationBuilder().Build();
                    services.AddSharedBffCookieAuthentication(configuration);
                });
                Microsoft.AspNetCore.Hosting.WebHostBuilderExtensions.Configure(builder, app => app
                    .UseRouting()
                    .UseAuthentication()
                    .UseAuthorization()
                    .UseEndpoints(endpoints =>
                    {
                        endpoints.MapSharedOidcAuthorizationCodeLogin(onSignedIn: BffOidcSignInHandler.HandleAsync);
                        endpoints.MapSharedBffLogout();
                        endpoints.MapSharedBffLogoutAllDevices();

                        // Representa lo que hace BffAccessTokenRequestTransform dentro del proxy YARP
                        // real (MapSharedBffProxy) -- exponer que el access token de la sesión
                        // server-side está disponible para reenviar a una API protegida, sin que la
                        // respuesta al navegador lo incluya en ningún otro lado.
                        endpoints.MapGet("/bff/api/whoami", async (HttpContext httpContext) =>
                        {
                            var token = await httpContext.GetTokenAsync(BffAuthenticationDefaults.Scheme, "access_token");
                            return Microsoft.AspNetCore.Http.Results.Ok(new { hasAccessToken = token is not null, tokenLength = token?.Length });
                        }).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = BffAuthenticationDefaults.Scheme });
                    }));
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static string ExtractCookieHeader(HttpResponseMessage response, string cookieName)
    {
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(cookieName + "=", StringComparison.Ordinal));
        return setCookie[..setCookie.IndexOf(';')];
    }

    private static async Task<string> LoginAndGetSessionCookieAsync(HttpClient client)
    {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var loginResponse = await client.SendAsync(loginRequest, HttpCompletionOption.ResponseHeadersRead);
        var correlationCookie = ExtractCookieHeader(loginResponse, "bc-oidc-cx");

        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/callback?code=valid-code&state=the-state");
        callbackRequest.Headers.Add("Cookie", correlationCookie);
        var callbackResponse = await client.SendAsync(callbackRequest);

        callbackResponse.StatusCode.Should().Be(HttpStatusCode.Redirect, "el signIn del BFF redirige a la SPA, nunca devuelve los tokens en el body");
        return ExtractCookieHeader(callbackResponse, BffCookieName);
    }

    [Fact]
    public async Task Callback_NeverIncludesTheAccessTokenAnywhereInTheResponse()
    {
        var server = await CreateServerAsync(accessToken: "super-secret-access-token");
        using var client = server.CreateClient();

        using var loginRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var loginResponse = await client.SendAsync(loginRequest, HttpCompletionOption.ResponseHeadersRead);
        var correlationCookie = ExtractCookieHeader(loginResponse, "bc-oidc-cx");

        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/callback?code=valid-code&state=the-state");
        callbackRequest.Headers.Add("Cookie", correlationCookie);
        var callbackResponse = await client.SendAsync(callbackRequest);

        var body = await callbackResponse.Content.ReadAsStringAsync();
        var allHeaders = string.Join('\n', callbackResponse.Headers.SelectMany(h => h.Value));

        body.Should().NotContain("super-secret-access-token");
        allHeaders.Should().NotContain("super-secret-access-token");
    }

    [Fact]
    public async Task Callback_SetsAnHttpOnlySecureSameSiteSessionCookie_WithAnOpaqueValue()
    {
        var server = await CreateServerAsync(accessToken: "super-secret-access-token");
        using var client = server.CreateClient();

        using var loginRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var loginResponse = await client.SendAsync(loginRequest, HttpCompletionOption.ResponseHeadersRead);
        var correlationCookie = ExtractCookieHeader(loginResponse, "bc-oidc-cx");

        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/callback?code=valid-code&state=the-state");
        callbackRequest.Headers.Add("Cookie", correlationCookie);
        var callbackResponse = await client.SendAsync(callbackRequest);

        var setCookie = callbackResponse.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(BffCookieName + "=", StringComparison.Ordinal));
        setCookie.Should().Contain("httponly");
        setCookie.Should().Contain("secure");
        setCookie.Should().Contain("samesite=strict");
        setCookie.Should().NotContain("super-secret-access-token");
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithTheSessionCookie_CanRetrieveTheStoredAccessToken()
    {
        var server = await CreateServerAsync(accessToken: "super-secret-access-token");
        using var client = server.CreateClient();
        var sessionCookie = await LoginAndGetSessionCookieAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/api/whoami");
        request.Headers.Add("Cookie", sessionCookie);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"hasAccessToken\":true");
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithoutASessionCookie_Returns401()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/bff/api/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesTheSession_SoTheCookieNoLongerAuthenticates()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();
        var sessionCookie = await LoginAndGetSessionCookieAsync(client);

        using var whoamiBeforeLogout = new HttpRequestMessage(HttpMethod.Get, "/bff/api/whoami");
        whoamiBeforeLogout.Headers.Add("Cookie", sessionCookie);
        (await client.SendAsync(whoamiBeforeLogout)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        logoutRequest.Headers.Add("Cookie", sessionCookie);
        var logoutResponse = await client.SendAsync(logoutRequest);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK, "el IdP simulado publica end_session_endpoint");
        var logoutBody = await logoutResponse.Content.ReadAsStringAsync();
        logoutBody.Should().Contain("idpEndSessionUri");

        using var whoamiAfterLogout = new HttpRequestMessage(HttpMethod.Get, "/bff/api/whoami");
        whoamiAfterLogout.Headers.Add("Cookie", sessionCookie);
        var afterLogoutResponse = await client.SendAsync(whoamiAfterLogout);
        afterLogoutResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "la sesión server-side fue revocada, la misma cookie ya no debe autenticar nada");
    }

    [Fact]
    public async Task LogoutAllDevices_RevokesEverySessionOfTheSameSubject_NotJustTheCallingOne()
    {
        // F2-06, "cerrar sesión en todos los dispositivos": el id_token de juguete de CreateServerAsync
        // siempre usa "sub"="user-1" -- dos logins independientes en el mismo servidor representan dos
        // sesiones del MISMO usuario en dos dispositivos/navegadores distintos (cada uno con su propia
        // cookie de sesión opaca, ver LoginAndGetSessionCookieAsync).
        var server = await CreateServerAsync();
        using var client = server.CreateClient();
        var firstDeviceCookie = await LoginAndGetSessionCookieAsync(client);
        var secondDeviceCookie = await LoginAndGetSessionCookieAsync(client);

        using var logoutAllRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/logout-all");
        logoutAllRequest.Headers.Add("Cookie", firstDeviceCookie);
        var logoutAllResponse = await client.SendAsync(logoutAllRequest);
        logoutAllResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var whoamiFirstDevice = new HttpRequestMessage(HttpMethod.Get, "/bff/api/whoami");
        whoamiFirstDevice.Headers.Add("Cookie", firstDeviceCookie);
        (await client.SendAsync(whoamiFirstDevice)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "la sesión desde la que se pidió el logout-all también debe quedar revocada");

        using var whoamiSecondDevice = new HttpRequestMessage(HttpMethod.Get, "/bff/api/whoami");
        whoamiSecondDevice.Headers.Add("Cookie", secondDeviceCookie);
        (await client.SendAsync(whoamiSecondDevice)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "logout-all debe revocar TODAS las sesiones del mismo sujeto, no solo la que hizo la llamada");
    }

    [Fact]
    public async Task LogoutAllDevices_WithoutASessionCookie_Returns401_AndDoesNotRevokeAnySession()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();
        var sessionCookie = await LoginAndGetSessionCookieAsync(client);

        var unauthenticatedLogoutAllResponse = await client.PostAsync("/auth/logout-all", content: null);

        unauthenticatedLogoutAllResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var whoami = new HttpRequestMessage(HttpMethod.Get, "/bff/api/whoami");
        whoami.Headers.Add("Cookie", sessionCookie);
        (await client.SendAsync(whoami)).StatusCode.Should().Be(HttpStatusCode.OK,
            "una llamada a logout-all sin autenticar no debe afectar ninguna sesión existente");
    }

    [Fact]
    public async Task Login_WithoutAnIdToken_FailsInsteadOfCreatingASessionWithoutIdentity()
    {
        var server = await CreateServerAsync(idToken: string.Empty);
        using var client = server.CreateClient();

        using var loginRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        var loginResponse = await client.SendAsync(loginRequest, HttpCompletionOption.ResponseHeadersRead);
        var correlationCookie = ExtractCookieHeader(loginResponse, "bc-oidc-cx");

        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/callback?code=valid-code&state=the-state");
        callbackRequest.Headers.Add("Cookie", correlationCookie);
        var callbackResponse = await client.SendAsync(callbackRequest);

        callbackResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError, "BffOidcSignInHandler mapea la falta de id_token a Error.Failure (ResultExtensions.ToProblemDetails)");
        // El callback SIEMPRE borra la cookie de correlación de F2-02 (Set-Cookie con MaxAge negativo),
        // haya o no fallado -- lo relevante para F2-03 es que nunca se emite la cookie de SESIÓN del BFF
        // sin identidad.
        var setCookieHeaders = callbackResponse.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
        setCookieHeaders.Should().NotContain(v => v.StartsWith(BffCookieName + "=", StringComparison.Ordinal));
    }
}
