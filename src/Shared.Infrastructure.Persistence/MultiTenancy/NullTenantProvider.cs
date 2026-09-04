using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;

public sealed class NullTenantProvider : ITenantProvider
{
    public bool IsMultiTenancyEnabled => false;

    public Guid? TenantId => null;
}
