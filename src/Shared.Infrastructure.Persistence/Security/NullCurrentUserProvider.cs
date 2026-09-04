using BitCode.Framework.Shared.Domain.Security;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Security;

public sealed class NullCurrentUserProvider : ICurrentUserProvider
{
    public string? UserId => null;
}
