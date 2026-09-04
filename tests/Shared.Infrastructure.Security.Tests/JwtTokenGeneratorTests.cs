using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
