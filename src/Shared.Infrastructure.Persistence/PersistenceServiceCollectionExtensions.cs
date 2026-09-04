using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Security;
using BitCode.Framework.Shared.Infrastructure.Persistence.Interceptors;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.Repositories;
using BitCode.Framework.Shared.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore; // UseSqlServer (Microsoft.EntityFrameworkCore.SqlServer)
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registra un MultiTenantDbContext consumidor sobre SQL Server, con los interceptores de
    /// auditoría/soft-delete/tenant ya conectados, Unit of Work y el repositorio genérico
    /// (IRepository&lt;,&gt;) resueltos contra ese contexto. ITenantProvider e ICurrentUserProvider
    /// se registran con implementaciones no-op por defecto (TryAdd): un proyecto multi-tenant o con
    /// usuario autenticado debe registrar sus propias implementaciones ANTES de llamar a este método.
    /// </summary>
    public static IServiceCollection AddSharedPersistence<TContext>(
        this IServiceCollection services,
        string connectionString)
        where TContext : MultiTenantDbContext
    {
        services.TryAddScoped<ITenantProvider, NullTenantProvider>();
        services.TryAddScoped<ICurrentUserProvider, NullCurrentUserProvider>();

        services.AddScoped<AuditableEntitySaveChangesInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<TenantSaveChangesInterceptor>();

        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString);
            options.AddInterceptors(
                sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<TenantSaveChangesInterceptor>());
        });

        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<TContext>()));
        services.AddScoped(typeof(IRepository<,>), typeof(RepositoryBase<,>));

        return services;
    }
}
