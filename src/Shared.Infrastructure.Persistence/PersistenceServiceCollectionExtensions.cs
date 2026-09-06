using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Security;
using BitCode.Framework.Shared.Infrastructure.Persistence.Interceptors;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;
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
        where TContext : MultiTenantDbContext =>
        services.AddSharedPersistence<TContext>(connectionString, static _ => { });

    /// <summary>
    /// Sobrecarga que además acepta <see cref="PersistenceOptions"/> (F1-10: por ejemplo,
    /// <see cref="PersistenceOptions.CommandTimeoutSeconds"/> para acotar cuánto puede tardar un
    /// comando SQL individual, independiente del <see cref="System.Threading.CancellationToken"/>
    /// del request).
    /// </summary>
    public static IServiceCollection AddSharedPersistence<TContext>(
        this IServiceCollection services,
        string connectionString,
        Action<PersistenceOptions> configureOptions)
        where TContext : MultiTenantDbContext
    {
        var persistenceOptions = new PersistenceOptions();
        configureOptions(persistenceOptions);

        services.TryAddScoped<ITenantProvider, NullTenantProvider>();
        services.TryAddScoped<ICurrentUserProvider, NullCurrentUserProvider>();

        // F1-15: ITenantContext envuelve el ITenantProvider ya registrado arriba (el que haya
        // ganado — NullTenantProvider por defecto, o el que el proyecto consumidor haya registrado
        // antes de este método, p. ej. HttpContextTenantProvider) y memoiza el TenantId resuelto
        // para todo el scope. No es una alternativa a ITenantProvider: MultiTenantDbContext y
        // TenantSaveChangesInterceptor siguen consumiendo ITenantProvider directamente (F1-12), sin
        // cambios, para no introducir un breaking change en el filtro global de EF Core ya
        // productivo.
        services.TryAddScoped<ITenantContext, TenantContext>();

        // F1-13 (estrategia T2, contratos y prototipo): por defecto todo tenant resuelve al shard
        // T1 (base de datos compartida) contra la misma connectionString ya configurada — cero
        // cambio de comportamiento para un proyecto que no optó explícitamente por sharding. Un
        // proyecto que sí lo necesite reemplaza IShardResolver (p. ej. por TenantShardMapResolver)
        // ANTES de llamar a este método, igual que con ITenantProvider.
        services.TryAddScoped<IShardResolver, SharedDatabaseShardResolver>();
        services.TryAddSingleton<IShardConnectionStringProvider>(
            _ => new SingleConnectionStringShardProvider(connectionString));

        // F1-14 (estrategia T3, contratos y operación): por defecto ningún tenant tiene base de
        // datos dedicada — un catálogo vacío es "cero cambio de comportamiento" para T1/T2, igual
        // que los defaults de IShardResolver/IShardConnectionStringProvider de arriba. Un proyecto
        // que adopte T3 reemplaza IDedicatedTenantDatabaseCatalog por una implementación productiva
        // (tabla de control en SQL Server) ANTES de llamar a este método.
        services.TryAddSingleton<IDedicatedTenantDatabaseCatalog, InMemoryDedicatedTenantDatabaseCatalog>();
        services.TryAddScoped<DedicatedTenantDatabaseMigrator>();

        services.AddScoped<AuditableEntitySaveChangesInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<TenantSaveChangesInterceptor>();

        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sqlServerOptions =>
            {
                if (persistenceOptions.CommandTimeoutSeconds is { } commandTimeoutSeconds)
                {
                    sqlServerOptions.CommandTimeout(commandTimeoutSeconds);
                }
            });
            options.AddInterceptors(
                sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<TenantSaveChangesInterceptor>());
        });

        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<TContext>()));
        services.AddScoped(typeof(IRepository<,>), typeof(RepositoryBase<,>));
        // IReadRepository<,> también resuelve a RepositoryBase<,>: sin este registro, un IQuery
        // (Fase 2) que solo necesita lectura no puede inyectar IReadRepository<,> — solo
        // IRepository<,> quedaba resoluble, aunque RepositoryBase implementa ambas interfaces.
        services.AddScoped(typeof(IReadRepository<,>), typeof(RepositoryBase<,>));

        return services;
    }
}
