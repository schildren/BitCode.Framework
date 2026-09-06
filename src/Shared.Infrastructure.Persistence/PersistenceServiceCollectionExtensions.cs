using BitCode.Framework.Shared.Domain.Idempotency;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Security;
using BitCode.Framework.Shared.Infrastructure.Persistence.HotPaths;
using BitCode.Framework.Shared.Infrastructure.Persistence.Idempotency;
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
        // F1-23 (Outbox base): escribe cada DomainEvent pendiente de los agregados trackeados como una
        // fila OutboxMessages en el MISMO SaveChangesAsync que persiste el cambio de negocio.
        services.AddScoped<OutboxSaveChangesInterceptor>();

        // F1-19: se registra con AddDbContext (Scoped), deliberadamente sin pooling
        // (AddDbContextPool/PooledDbContextFactory). MultiTenantDbContext resuelve el TenantId en su
        // constructor y lo cierra sobre el filtro global de tenancy; el pooling de EF Core reutiliza la
        // misma instancia entre scopes sin volver a invocar el constructor, lo que congelaría el
        // filtro con el tenant que construyó la instancia por primera vez. Además, mientras
        // OnConfiguring siga reemplazando IModelCacheKeyFactory por PerInstanceModelCacheKeyFactory
        // (necesario para la corrección del filtro de tenant, ver F1-11), EF Core rechaza el pooling en
        // tiempo de ejecución con InvalidOperationException. Ver docs/adr/0012-dbcontext-pooling-no-adoptado.md.
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
                sp.GetRequiredService<TenantSaveChangesInterceptor>(),
                sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });

        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<TContext>()));
        // F1-22: comparte el mismo DbContext de scope que IUnitOfWork/IRepository de arriba — el
        // registro de idempotencia se persiste en el mismo SaveChangesAsync que el efecto del
        // comando, coordinado por IdempotencyBehavior (Shared.Application).
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped(typeof(IRepository<,>), typeof(RepositoryBase<,>));
        // IReadRepository<,> resuelve a ReadOnlyRepositoryBase<,> (F1-17), no a RepositoryBase<,>:
        // un IQuery (Fase 2) nunca muta datos (regla dura #2 de docs/convenciones.md), así que toda
        // lectura resuelta por esta vía puede forzar AsNoTracking() sin excepción — el change tracker
        // de EF Core no aporta nada cuando la entidad jamás se va a pasar a SaveChangesAsync.
        // RepositoryBase<,> (detrás de IRepository<,>) sigue trackeando por defecto porque el lado de
        // escritura necesita GetByIdAsync trackeado para poder llamar Update sobre esa misma instancia.
        services.AddScoped(typeof(IReadRepository<,>), typeof(ReadOnlyRepositoryBase<,>));

        // F1-18: contrato de extensión controlada para hot paths que ISpecification<T> no expresa
        // bien (ver docs/guia-hot-paths.md). Comparte el mismo DbContext de scope que IRepository/
        // IReadRepository/IUnitOfWork de arriba — no abre una conexión ni un DbContext propio.
        services.AddScoped<IHotPathQueryExecutor, HotPathQueryExecutor>();

        return services;
    }
}
