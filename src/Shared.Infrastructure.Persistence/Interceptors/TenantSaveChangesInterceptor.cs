using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Interceptors;

public class TenantSaveChangesInterceptor(ITenantProvider tenantProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AssignTenantId(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AssignTenantId(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AssignTenantId(DbContext? context)
    {
        if (context is null || !tenantProvider.IsMultiTenancyEnabled || tenantProvider.TenantId is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<ITenantEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = tenantProvider.TenantId.Value;
            }
        }
    }
}
