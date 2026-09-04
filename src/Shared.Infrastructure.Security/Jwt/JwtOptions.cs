namespace BitCode.Framework.Shared.Infrastructure.Security.Jwt;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string SecretKey { get; set; }

    public required string Issuer { get; set; }

    public required string Audience { get; set; }

    public int AccessTokenExpirationMinutes { get; set; } = 15;

    public int RefreshTokenExpirationDays { get; set; } = 7;
}
