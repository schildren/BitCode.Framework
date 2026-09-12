using System.Net;
using BitCode.Framework.Shared.Infrastructure.Web.Security.Bff;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Transforms;

namespace BitCode.Framework.Shared.Infrastructure.Web.Tests.Security.Bff;

/// <summary>
/// F2-03, criterio de aceptación "Tokens no quedan expuestos al navegador": verifica, contra un
/// pipeline HTTP real (<see cref="TestServer"/>, sin YARP ni un servidor downstream reales) que
/// <see cref="BffAccessTokenRequestTransform"/> adjunta el access token guardado en la sesión
/// server-side del usuario autenticado como header <c>Authorization: Bearer</c> de la request
/// reenviada -- el mecanismo concreto que le permite a la SPA llamar a las APIs protegidas sin tener,
/// ella misma, ningún token.
/// </summary>
public class BffAccessTokenRequestTransformTests
{
    private static async Task<TestServer> CreateServerAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthorization();
                    services.AddDataProtection();
                    services.AddAuthentication(BffAuthenticationDefaults.Scheme).AddCookie(BffAuthenticationDefaults.Scheme);
                });
                Microsoft.AspNetCore.Hosting.WebHostBuilderExtensions.Configure(builder, app => app
                    .UseRouting()
                    .UseAuthentication()
                    .UseAuthorization()
                    .UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/test/login", async (HttpContext httpContext, string accessToken) =>
                        {
                            var principal = new System.Security.Claims.ClaimsPrincipal(
                                new System.Security.Claims.ClaimsIdentity(BffAuthenticationDefaults.Scheme));
                            var properties = new AuthenticationProperties();
                            properties.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = accessToken } });
                            await httpContext.SignInAsync(BffAuthenticationDefaults.Scheme, principal, properties);
                            return Microsoft.AspNetCore.Http.Results.Ok();
                        });

                        // Ejercita literalmente la misma llamada que hace BffAccessTokenRequestTransform
                        // dentro del pipeline de YARP real (MapSharedBffProxy) -- sin necesitar un
                        // servidor downstream ni un cluster de YARP configurado para esta prueba.
                        endpoints.MapGet("/test/apply-transform", async (HttpContext httpContext) =>
                        {
                            using var proxyRequest = new HttpRequestMessage();
                            if (httpContext.Request.Headers.TryGetValue("X-Simulated-Browser-Authorization", out var browserAuth))
                            {
                                proxyRequest.Headers.TryAddWithoutValidation("Authorization", (string?)browserAuth);
                            }

                            var context = new RequestTransformContext { HttpContext = httpContext, ProxyRequest = proxyRequest };
                            await new BffAccessTokenRequestTransform().ApplyAsync(context);

                            return Microsoft.AspNetCore.Http.Results.Ok(new
                            {
                                scheme = proxyRequest.Headers.Authorization?.Scheme,
                                parameter = proxyRequest.Headers.Authorization?.Parameter,
                            });
                        }).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = BffAuthenticationDefaults.Scheme });
                    }));
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static string ExtractCookieHeader(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single();

    [Fact]
    public async Task ApplyAsync_WithAnAuthenticatedSession_AttachesTheStoredAccessTokenAsABearerHeader()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        var loginResponse = await client.GetAsync("/test/login?accessToken=access-token-from-session");
        var sessionCookie = ExtractCookieHeader(loginResponse);
        sessionCookie[..sessionCookie.IndexOf(';')].Should().NotContain("access-token-from-session", "el navegador nunca debe recibir el access token, ni siquiera dentro de la cookie");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/apply-transform");
        request.Headers.Add("Cookie", sessionCookie[..sessionCookie.IndexOf(';')]);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"scheme\":\"Bearer\"");
        body.Should().Contain("\"parameter\":\"access-token-from-session\"");
    }

    [Fact]
    public async Task ApplyAsync_OverwritesAnyAuthorizationHeaderTheBrowserMayHaveSent()
    {
        // Defensa en profundidad: aunque la SPA no debería mandar Authorization (no tiene el token),
        // si algo lo hiciera, el proxy nunca debe reenviarlo tal cual -- siempre usa el de la sesión.
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        var loginResponse = await client.GetAsync("/test/login?accessToken=access-token-from-session");
        var sessionCookie = ExtractCookieHeader(loginResponse);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/apply-transform");
        request.Headers.Add("Cookie", sessionCookie[..sessionCookie.IndexOf(';')]);
        request.Headers.Add("X-Simulated-Browser-Authorization", "Bearer token-que-mando-el-navegador");
        var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"parameter\":\"access-token-from-session\"");
        body.Should().NotContain("token-que-mando-el-navegador");
    }

    [Fact]
    public async Task ApplyAsync_WithoutASessionCookie_NeverReachesTheTransform()
    {
        // Este test usa un AddCookie "de fábrica" (sin los overrides de eventos de
        // AddSharedBffCookieAuthentication que devuelven 401 para llamadas AJAX) -- lo relevante acá
        // es que, sin sesión válida, el pipeline de autorización nunca deja pasar la request hasta el
        // endpoint que ejecuta BffAccessTokenRequestTransform (nunca hay un 200 con Authorization).
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/test/apply-transform");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
