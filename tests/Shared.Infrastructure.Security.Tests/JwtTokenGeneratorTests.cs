using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

public class JwtTokenGeneratorTests
{
    private static readonly JwtOptions Options = new()
    {
        SecretKey = "una-clave-secreta-de-al-menos-32-caracteres-para-hmacsha256",
        Issuer = "BitCode.Framework.Tests",
        Audience = "BitCode.Framework.Tests.Clients",
        AccessTokenExpirationMinutes = 15,
    };

    private static JwtTokenGenerator CreateGenerator() => new(Microsoft.Extensions.Options.Options.Create(Options));

    [Fact]
    public void GenerateAccessToken_IncludesUserIdRolesAndExtraClaims()
    {
        var generator = CreateGenerator();
        var user = new TestApplicationUser { Id = Guid.NewGuid(), UserName = "jperez", Email = "jperez@test.com" };

        var token = generator.GenerateAccessToken(
            user,
            ["Admin"],
            [new Claim("permission", "productos.crear")]);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Subject.Should().Be(user.Id.ToString());
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        jwt.Claims.Should().Contain(c => c.Type == "permission" && c.Value == "productos.crear");
        jwt.Issuer.Should().Be(Options.Issuer);
        jwt.Audiences.Should().Contain(Options.Audience);
    }

    [Fact]
    public void GenerateAccessToken_SetsExpirationAccordingToOptions()
    {
        var generator = CreateGenerator();
        var user = new TestApplicationUser { Id = Guid.NewGuid(), UserName = "jperez" };

        var token = generator.GenerateAccessToken(user, [], []);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.ValidTo.Should().BeCloseTo(
            DateTime.UtcNow.AddMinutes(Options.AccessTokenExpirationMinutes),
            TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// F1-12: el TenantId del usuario viaja como claim firmado dentro del propio JWT — es la fuente
    /// que HttpContextTenantProvider (Shared.Infrastructure.Web) usa para resolver el tenant, nunca
    /// un header/query string controlado por el cliente.
    /// </summary>
    [Fact]
    public void GenerateAccessToken_IncludesTenantClaim_WhenUserHasTenantId()
    {
        var generator = CreateGenerator();
        var tenantId = Guid.NewGuid();
        var user = new TestApplicationUser { Id = Guid.NewGuid(), UserName = "jperez", TenantId = tenantId };

        var token = generator.GenerateAccessToken(user, [], []);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == TenantClaimTypes.TenantId && c.Value == tenantId.ToString());
    }

    /// <summary>
    /// Guid.Empty representa "sin tenant asignado" (proyecto de un solo tenant): no debe emitirse
    /// como si fuera un tenant real, porque HttpContextTenantProvider trataría Guid.Empty como un
    /// tenant válido en vez de tratarlo como "sin claim".
    /// </summary>
    [Fact]
    public void GenerateAccessToken_OmitsTenantClaim_WhenUserHasNoTenantId()
    {
        var generator = CreateGenerator();
        var user = new TestApplicationUser { Id = Guid.NewGuid(), UserName = "jperez", TenantId = Guid.Empty };

        var token = generator.GenerateAccessToken(user, [], []);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Claims.Should().NotContain(c => c.Type == TenantClaimTypes.TenantId);
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsUniqueValuesEachTime()
    {
        var generator = CreateGenerator();

        var first = generator.GenerateRefreshToken();
        var second = generator.GenerateRefreshToken();

        first.Should().NotBe(second);
        first.Should().NotBeNullOrWhiteSpace();
    }
}
