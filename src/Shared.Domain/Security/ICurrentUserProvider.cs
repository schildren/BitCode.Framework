namespace BitCode.Framework.Shared.Domain.Security;

public interface ICurrentUserProvider
{
    string? UserId { get; }
}
