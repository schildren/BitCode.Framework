using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

public class FakeTenantProvider(Guid? tenantId, bool isMultiTenancyEnabled = true) : ITenantProvider
{
    public bool IsMultiTenancyEnabled { get; } = isMultiTenancyEnabled;

    public Guid? TenantId { get; } = tenantId;
}
