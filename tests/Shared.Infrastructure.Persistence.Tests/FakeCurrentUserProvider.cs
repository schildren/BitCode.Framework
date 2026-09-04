using BitCode.Framework.Shared.Domain.Security;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class FakeCurrentUserProvider(string? userId) : ICurrentUserProvider
{
    public string? UserId { get; } = userId;
}
