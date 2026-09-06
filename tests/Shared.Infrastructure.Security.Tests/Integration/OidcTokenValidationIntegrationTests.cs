using System.Net;
using System.Net.Http.Headers;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Integration;

/// <summary>
/// F2-05, criterio de aceptación "casos negativos automatizados": ejercita
/// <see cref="OidcAuthenticationServiceCollectionExtensions.AddSharedOidcAuthentication"/> de punta a
/// punta contra un Keycloak real (Testcontainers, ADR 0004) -- issuer, audience, firma y vigencia se
/// validan tal como los validaría cualquier API protegida por el adapter, sin mocks del middleware
/// JwtBearer ni de <c>TokenValidationParameters</c>. Un endpoint mínimo protegido con
/// <c>RequireAuthorization()</c> hace de doble de la API real: 200 significa "token aceptado", 401
/// significa "rechazado" (el motivo exacto queda en <c>WWW-Authenticate</c>, que también se verifica
/// donde aporta valor).
/// </summary>
[Collection(KeycloakCollection.Name)]
public class OidcTokenValidationIntegrationTests(KeycloakContainerFixture fixture)
{
    private async Task<HttpClient> BuildProtectedApiClientAsync(Action<OidcOptions>? mutateOptions = null)
    {
        var options = new OidcOptions
        {
            Authority = fixture.Authority,
            Audience = KeycloakContainerFixture.ClientId,
            // Testcontainers levanta Keycloak en HTTP plano (sslRequired "none" en el realm de
            // prueba) -- exigir HTTPS de metadata solo tendría sentido para un IdP real en producción.
            RequireHttpsMetadata = false,
        };
        mutateOptions?.Invoke(options);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Authority"] = options.Authority,
                ["Oidc:Audience"] = options.Audience,
                ["Oidc:RequireHttpsMetadata"] = options.RequireHttpsMetadata.ToString(),
                ["Oidc:ClockSkew"] = options.ClockSkew.ToString(),
                ["Oidc:JwksMinimumRefreshInterval"] = options.JwksMinimumRefreshInterval?.ToString(),
                ["Oidc:JwksAutomaticRefreshInterval"] = options.JwksAutomaticRefreshInterval?.ToString(),
            })
            .Build();

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthorization();
                    services.AddSharedOidcAuthentication(configuration);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/protected", () => Results.Ok("ok")).RequireAuthorization();
                    });
                });
            });

        var host = await hostBuilder.StartAsync();
        return host.GetTestClient();
    }

    private static async Task<HttpStatusCode> CallProtectedEndpointAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>Corrompe el tercer segmento (firma) de un JWT compacto, dejando header y payload intactos.</summary>
    private static string TamperSignature(string jwt)
    {
        var segments = jwt.Split('.');
        segments[2] = segments[2].Length > 0
            ? (segments[2][0] == 'A' ? 'B' : 'A') + segments[2][1..]
            : "tampered";
        return string.Join('.', segments);
    }

    [Fact]
    public async Task Token_Valido_EsAceptado()
    {
        using var client = await BuildProtectedApiClientAsync();
        var accessToken = await fixture.RequestAccessTokenAsync();

        var status = await CallProtectedEndpointAsync(client, accessToken);

        status.Should().Be(HttpStatusCode.OK,
            "un token real, recién emitido, con issuer/audience/firma correctos, debe ser aceptado (control positivo)");
    }

    [Fact]
    public async Task Token_Expirado_EsRechazado()
    {
        // ClockSkew=0 para aislar exclusivamente ValidateLifetime -- el caso "vencido pero dentro del
        // margen de tolerancia" es un escenario propio (ver los tests de clock skew más abajo), no una
        // ambigüedad de este test.
        using var client = await BuildProtectedApiClientAsync(o => o.ClockSkew = TimeSpan.Zero);
        var accessToken = await fixture.RequestAccessTokenAsync();

        // El realm de prueba fija accessTokenLifespan en unos pocos segundos (ver
        // KeycloakContainerFixture) -- esperar más que eso garantiza vigencia vencida sin ClockSkew
        // que la absorba.
        await Task.Delay(TimeSpan.FromSeconds(KeycloakContainerFixture.AccessTokenLifespanSeconds + 3));

        var status = await CallProtectedEndpointAsync(client, accessToken);

        status.Should().Be(HttpStatusCode.Unauthorized, "un token cuyo 'exp' ya pasó debe rechazarse (ValidateLifetime)");
    }

    [Fact]
    public async Task Token_ConFirmaAlterada_EsRechazado()
    {
        using var client = await BuildProtectedApiClientAsync();
        var accessToken = await fixture.RequestAccessTokenAsync();
        var tamperedToken = TamperSignature(accessToken);

        var status = await CallProtectedEndpointAsync(client, tamperedToken);

        status.Should().Be(HttpStatusCode.Unauthorized,
            "un token con la firma modificada (mismo header/payload, distinta firma) nunca debe aceptarse (ValidateIssuerSigningKey)");
    }

    [Fact]
    public async Task Token_ConIssuerIncorrecto_EsRechazado()
    {
        // El token viene de un segundo realm real de Keycloak (issuer y claves de firma propios, ver
        // KeycloakContainerFixture.OtherRealmName) -- forzar solo TokenValidationParameters.ValidIssuer
        // no sirve para aislar este caso porque JwtBearerHandler concatena siempre el issuer real
        // resuelto por OIDC Discovery a los issuers válidos; la única forma realista de probar un
        // issuer incorrecto es presentar un token que efectivamente venga de otro emisor.
        using var client = await BuildProtectedApiClientAsync();
        var accessTokenDeOtroRealm = await fixture.RequestAccessTokenFromOtherRealmAsync();

        var status = await CallProtectedEndpointAsync(client, accessTokenDeOtroRealm);

        status.Should().Be(HttpStatusCode.Unauthorized,
            "un token cuyo 'iss' no coincide con el issuer configurado (emitido por otro realm) debe rechazarse");
    }

    [Fact]
    public async Task Token_ConAudienceIncorrecta_EsRechazado()
    {
        using var clientWithWrongAudience = await BuildProtectedApiClientWithConfigureAsync(options =>
            options.TokenValidationParameters.ValidAudience = "otra-audiencia-que-no-es-bitcode-api");
        var accessToken = await fixture.RequestAccessTokenAsync();

        var status = await CallProtectedEndpointAsync(clientWithWrongAudience, accessToken);

        status.Should().Be(HttpStatusCode.Unauthorized, "un token cuyo 'aud' no coincide con la audience esperada debe rechazarse (ValidateAudience)");
    }

    [Fact]
    public async Task Token_FueraDeVigenciaPeroDentroDelClockSkew_EsAceptado()
    {
        // ClockSkew amplio (30s, el default de OidcOptions) debe absorber un token técnicamente
        // vencido hace pocos segundos -- exactamente el escenario que justifica que ClockSkew exista.
        using var clientConClockSkewAmplio = await BuildProtectedApiClientAsync(o => o.ClockSkew = TimeSpan.FromSeconds(30));
        var accessToken = await fixture.RequestAccessTokenAsync();

        await Task.Delay(TimeSpan.FromSeconds(KeycloakContainerFixture.AccessTokenLifespanSeconds + 3));

        var status = await CallProtectedEndpointAsync(clientConClockSkewAmplio, accessToken);

        status.Should().Be(HttpStatusCode.OK,
            "un token vencido hace pocos segundos debe aceptarse cuando ClockSkew cubre ese margen");
    }

    [Fact]
    public async Task Token_FueraDelClockSkewConfigurado_EsRechazado()
    {
        // Mismo escenario que el caso anterior, pero con ClockSkew=0: el margen de tolerancia ya no
        // cubre los segundos transcurridos desde la expiración, y el token debe rechazarse.
        using var clientSinClockSkew = await BuildProtectedApiClientAsync(o => o.ClockSkew = TimeSpan.Zero);
        var accessToken = await fixture.RequestAccessTokenAsync();

        await Task.Delay(TimeSpan.FromSeconds(KeycloakContainerFixture.AccessTokenLifespanSeconds + 3));

        var status = await CallProtectedEndpointAsync(clientSinClockSkew, accessToken);

        status.Should().Be(HttpStatusCode.Unauthorized,
            "sin tolerancia de reloj, un token vencido debe rechazarse aunque el desfase sea de pocos segundos");
    }

    [Fact]
    public async Task Token_FirmadoConClaveRotadaEnElIdp_EsAceptadoSinReiniciarLaAplicacion()
    {
        // F2-06, criterio de aceptación "Rotación sin downtime": JwksMinimumRefreshInterval bajo (el
        // piso técnico de Microsoft.IdentityModel es 1 segundo, ver BaseConfigurationManager) en vez
        // del default de 5 minutos, exclusivamente para que esta prueba no necesite esperar minutos
        // reales -- en producción ese default de 5 minutos es intencional (ver
        // OidcOptions.JwksMinimumRefreshInterval, protección anti-DoS contra el JWKS del IdP).
        using var client = await BuildProtectedApiClientAsync(o => o.JwksMinimumRefreshInterval = TimeSpan.FromSeconds(2));

        // Control positivo, ANTES de rotar: un token con la clave original se acepta y, como efecto
        // colateral necesario para el resto del test, obliga al middleware a resolver y cachear el JWKS
        // con la clave "vieja" -- exactamente el estado en el que un servicio real estaría en producción
        // en el momento en que el IdP rota su clave de firma.
        var tokenFirmadoConClaveOriginal = await fixture.RequestAccessTokenAsync();
        var statusAntesDeRotar = await CallProtectedEndpointAsync(client, tokenFirmadoConClaveOriginal);
        statusAntesDeRotar.Should().Be(HttpStatusCode.OK);

        // Rotación real en el IdP (Admin REST API de Keycloak, no un mock): a partir de aquí, los
        // tokens NUEVOS que Keycloak emita quedan firmados con una clave que el middleware de esta
        // aplicación todavía no conoce (JWKS cacheado antes de la rotación).
        await fixture.RotateSigningKeyAsync();
        var tokenFirmadoConClaveRotada = await fixture.RequestAccessTokenAsync();

        // Sin reiniciar el host de prueba (mismo IServiceProvider, mismo ConfigurationManager con el
        // JWKS ya cacheado): el token firmado con la clave rotada debe validarse igual. Esto es
        // exactamente lo que demuestra "rotación sin downtime" -- si RefreshOnIssuerKeyNotFound no
        // funcionara, este token se rechazaría con 401 hasta el próximo reinicio/redeploy.
        var statusDespuesDeRotar = await CallProtectedEndpointAsync(client, tokenFirmadoConClaveRotada);

        statusDespuesDeRotar.Should().Be(HttpStatusCode.OK,
            "un token firmado con una clave rotada en el IdP debe aceptarse sin reiniciar el proceso -- el middleware refresca el JWKS ante un 'kid' desconocido (RefreshOnIssuerKeyNotFound)");
    }

    private async Task<HttpClient> BuildProtectedApiClientWithConfigureAsync(Action<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions> configureJwtBearer)
    {
        var options = new OidcOptions
        {
            Authority = fixture.Authority,
            Audience = KeycloakContainerFixture.ClientId,
            RequireHttpsMetadata = false,
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Authority"] = options.Authority,
                ["Oidc:Audience"] = options.Audience,
                ["Oidc:RequireHttpsMetadata"] = "false",
            })
            .Build();

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthorization();
                    services.AddSharedOidcAuthentication(configuration, configureJwtBearer);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/protected", () => Results.Ok("ok")).RequireAuthorization();
                    });
                });
            });

        var host = await hostBuilder.StartAsync();
        return host.GetTestClient();
    }
}
