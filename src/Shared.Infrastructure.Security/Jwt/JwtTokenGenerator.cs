using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BitCode.Framework.Shared.Infrastructure.Security.Jwt;

public class JwtTokenGenerator(IOptions<JwtOptions> options) : IJwtTokenGenerator
{
    private readonly JwtOptions _options = options.Value;

    public string GenerateAccessToken(ApplicationUser user, IEnumerable<string> roles, IEnumerable<Claim> extraClaims)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        };

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName));
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        }

        // F1-12: el TenantId viaja como claim firmado dentro del propio JWT — nunca se resuelve
        // desde un header o query string que el cliente pueda manipular (docs/threat-model.md, S2).
        // Guid.Empty se omite a propósito: representa "sin tenant asignado" (proyecto de un solo
        // tenant, o ApplicationUser creado sin TenantId todavía) y no debe emitirse como si fuera un
        // tenant real — HttpContextTenantProvider trata la ausencia del claim como "sin tenant".
        if (user.TenantId != Guid.Empty)
        {
            claims.Add(new Claim(TenantClaimTypes.TenantId, user.TenantId.ToString()));
        }

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(extraClaims);

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenExpirationMinutes),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }
}
