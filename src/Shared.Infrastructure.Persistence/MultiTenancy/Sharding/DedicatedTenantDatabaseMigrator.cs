using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;

/// <summary>
/// Prototipo del mecanismo operativo de F1-14 para aplicar una migración (u otra operación
/// idempotente, como <c>EnsureCreatedAsync</c> en un entorno de prueba) a todas las bases de datos
/// dedicadas (estrategia T3) registradas en <see cref="IDedicatedTenantDatabaseCatalog"/>.
/// </summary>
/// <remarks>
/// Deliberadamente agnóstico del tipo de <c>DbContext</c> concreto de cada proyecto consumidor:
/// <see cref="MigrateAllAsync"/> recibe un delegado <c>migrateDatabaseAsync</c> que el proyecto
/// consumidor construye (normalmente: crear una instancia de su <c>MultiTenantDbContext</c> con la
/// cadena de conexión recibida y llamar <c>Database.MigrateAsync()</c>), en vez de que
/// Shared.Infrastructure.Persistence conozca o dependa de un <c>DbContext</c> específico de negocio.
/// <para>
/// Un fallo al migrar un tenant individual (por ejemplo, su servidor está temporalmente inaccesible)
/// <b>no</b> detiene el procesamiento de los tenants restantes — cada resultado queda registrado en
/// <see cref="DedicatedTenantMigrationReport"/> para que el operador reintente solo los tenants
/// fallidos. Una <see cref="OperationCanceledException"/> sí se propaga tal cual (no se atrapa como
/// un fallo de tenant más), siguiendo la regla 10 de <c>docs/convenciones.md</c>: una cancelación no
/// es un resultado de negocio, es la señal de que el llamador ya no quiere seguir esperando.
/// </para>
/// </remarks>
public sealed class DedicatedTenantDatabaseMigrator(
    IDedicatedTenantDatabaseCatalog catalog,
    IShardResolver shardResolver,
    IShardConnectionStringProvider connectionStringProvider)
{
    public async Task<DedicatedTenantMigrationReport> MigrateAllAsync(
        Func<string, CancellationToken, Task> migrateDatabaseAsync,
        CancellationToken cancellationToken)
    {
        var tenantIds = await catalog.GetDedicatedTenantIdsAsync(cancellationToken);
        var results = new List<DedicatedTenantMigrationResult>(tenantIds.Count);

        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var shardId = await shardResolver.ResolveShardAsync(tenantId, cancellationToken);
                var connectionString =
                    await connectionStringProvider.GetConnectionStringAsync(shardId, cancellationToken);
                await migrateDatabaseAsync(connectionString, cancellationToken);
                results.Add(DedicatedTenantMigrationResult.Success(tenantId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(DedicatedTenantMigrationResult.Failure(tenantId, ex));
            }
        }

        return new DedicatedTenantMigrationReport(results);
    }
}
