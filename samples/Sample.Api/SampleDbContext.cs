using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Sample.Api.Productos;

namespace Sample.Api;

public class SampleDbContext(DbContextOptions<SampleDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<Producto> Productos => Set<Producto>();
}
