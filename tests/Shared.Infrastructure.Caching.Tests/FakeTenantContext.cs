using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Caching.Tests;

/// <summary>Doble de prueba de <see cref="ITenantContext"/>, equivalente al <c>FakeTenantProvider</c>
/// usado en Shared.Infrastructure.Persistence.Tests/Shared.Infrastructure.Security.Tests, pero al
/// nivel de contrato que consume <see cref="TenantAwareCache"/> (F1-16).</summary>
public sealed class FakeTenantContext(Guid? tenantId, bool isMultiTenancyEnabled = true) : ITenantContext
{
    public bool IsMultiTenancyEnabled { get; } = isMultiTenancyEnabled;

    public Guid? TenantId { get; } = tenantId;
}
