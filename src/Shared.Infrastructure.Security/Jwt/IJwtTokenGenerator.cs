using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;

namespace BitCode.Framework.Shared.Infrastructure.Security.Jwt;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(ApplicationUser user, IEnumerable<string> roles, IEnumerable<Claim> extraClaims);

    string GenerateRefreshToken();
}
