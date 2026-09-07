using BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Integration;

/// <summary>
/// F4-03 (R-TEC-08): dos instancias de un consumidor real de <c>AddSharedOidcAuthorizationCodeFlow</c>
/// (F2-02) -- simulando dos réplicas del mismo pod -- deben compartir el key ring de Data Protection
/// cuando <c>Caching:RedisConnectionString</c> está configurado, para que una cookie de correlación
/// cifrada por una instancia sea descifrable por la otra. Sin esto, un balanceador sin afinidad de
/// sesión rompe el login OIDC de forma intermitente según a qué pod caiga el callback -- exactamente el
/// hallazgo documentado en <c>docs/auditoria-estado-runtime-f4-03.md</c> sección 3.
/// </summary>
[Collection(RedisCollection.Name)]
public class SharedDataProtectionRedisIntegrationTests(RedisContainerFixture fixture)
{
    private static IOidcAuthorizationCodeStateProtector BuildProtector(string redisConnectionString)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:RedisConnectionString"] = redisConnectionString,
                ["Oidc:Authority"] = "https://idp.local/realms/bitcode",
                ["Oidc:ClientId"] = "bitcode-sample",
                ["Oidc:AuthorizationCode:RedirectUri"] = "https://api.bitcode.local/auth/callback",
            })
            .Build();

        services.AddSharedOidcAuthorizationCodeFlow(configuration);
        var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOidcAuthorizationCodeStateProtector>();
    }

    [Fact]
    public void Protect_ByOneInstance_IsUnprotectableByAnotherInstance_WhenBothShareTheRedisKeyRing()
    {
        var state = new OidcAuthorizationCodeState(
            State: "state-value",
            CodeVerifier: "code-verifier-value",
            Nonce: "nonce-value",
            RedirectUri: "https://api.bitcode.local/auth/callback",
            ReturnUrl: "/dashboard");

        // Dos ServiceProvider independientes, cada uno con su propio proceso de arranque de Data
        // Protection -- exactamente lo que simula dos pods distintos de un mismo Deployment.
        var instanceA = BuildProtector(fixture.ConnectionString);
        var instanceB = BuildProtector(fixture.ConnectionString);

        var protectedByA = instanceA.Protect(state);
        var unprotectedByB = instanceB.Unprotect(protectedByA);

        // Si el key ring no se compartiera (hallazgo original de R-TEC-08), instanceB no tendría la
        // clave de instanceA y Unprotect atraparía la CryptographicException devolviendo null.
        unprotectedByB.Should().Be(state);
    }
}
