namespace BitCode.Framework.Shared.Domain.MultiTenancy;

public interface ITenantProvider
{
    bool IsMultiTenancyEnabled { get; }

    Guid? TenantId { get; }
}
