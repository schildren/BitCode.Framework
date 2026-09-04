namespace BitCode.Framework.Shared.Infrastructure.Security.Jwt;

public class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Token { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;
}
