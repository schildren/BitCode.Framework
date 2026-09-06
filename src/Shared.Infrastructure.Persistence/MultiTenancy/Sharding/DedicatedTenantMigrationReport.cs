namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;

/// <summary>
/// Reporte agregado de <see cref="DedicatedTenantDatabaseMigrator.MigrateAllAsync"/> (F1-14):
/// resultado de aplicar una operación (típicamente una migración de EF Core) a cada base de datos
/// dedicada (T3) conocida por <c>IDedicatedTenantDatabaseCatalog</c>.
/// </summary>
public sealed record DedicatedTenantMigrationReport(IReadOnlyList<DedicatedTenantMigrationResult> Results)
{
    /// <summary>
    /// <see langword="true"/> si no hubo ni un solo tenant fallido. Un reporte con
    /// <see cref="Results"/> vacío (ningún tenant T3 registrado todavía) también es
    /// <see langword="true"/>: no hay nada que haya fallado.
    /// </summary>
    public bool AllSucceeded => Results.All(result => result.Succeeded);

    public IReadOnlyList<DedicatedTenantMigrationResult> Failures =>
        Results.Where(result => !result.Succeeded).ToArray();
}
