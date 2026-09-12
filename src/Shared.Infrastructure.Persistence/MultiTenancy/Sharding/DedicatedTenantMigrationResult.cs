namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;

/// <summary>
/// Resultado de aplicar la migración de un tenant individual dentro de
/// <see cref="DedicatedTenantDatabaseMigrator.MigrateAllAsync"/> (F1-14, prototipo de operación
/// masiva sobre bases dedicadas T3).
/// </summary>
/// <remarks>
/// Deliberadamente no usa <c>Result</c> (Shared.Kernel): ese tipo modela el resultado único de un
/// comando/query CQRS (ver <c>docs/convenciones.md</c>, regla 6), mientras que esto es el reporte de
/// un job operativo por lotes que procesa N tenants independientes — cada uno puede fallar sin que
/// eso invalide el resultado de los demás, y el reporte agregado (<see cref="DedicatedTenantMigrationReport"/>)
/// necesita conservar el detalle de cada tenant, no solo éxito/fallo global.
/// </remarks>
public sealed record DedicatedTenantMigrationResult(Guid TenantId, bool Succeeded, Exception? Error)
{
    public static DedicatedTenantMigrationResult Success(Guid tenantId) => new(tenantId, true, null);

    public static DedicatedTenantMigrationResult Failure(Guid tenantId, Exception error) =>
        new(tenantId, false, error);
}
