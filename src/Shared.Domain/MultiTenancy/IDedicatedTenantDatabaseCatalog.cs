namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Catálogo de tenants provisionados en la estrategia T3 (F1-14 — base de datos dedicada por
/// tenant). No reemplaza a <see cref="ITenantShardMapStore"/>: ese contrato responde "¿a qué
/// <see cref="ShardId"/> pertenece este tenant?" para un tenant conocido; este contrato responde
/// "¿qué tenants tienen hoy una base de datos dedicada?", una pregunta que T2 (F1-13) no necesitaba
/// resolver porque su prototipo nunca requirió operar en lote sobre todos los tenants con
/// asignación explícita (por ejemplo, para aplicar migraciones de EF Core a todas sus bases físicas
/// una por una).
/// </summary>
/// <remarks>
/// T3 se modela como un caso particular de T2 (ver ADR 0011): un tenant en T3 tiene una fila en
/// <see cref="ITenantShardMapStore"/> cuyo <see cref="ShardId"/> es exclusivo de ese tenant (nunca
/// compartido con otro), y <see cref="IShardConnectionStringProvider"/> resuelve ese
/// <see cref="ShardId"/> a la cadena de conexión de su base dedicada. Este catálogo es el único
/// contrato nuevo que introduce F1-14: enumerar todos los tenants dedicados es lo que permite operar
/// en lote (migraciones masivas, inventario para provisioning/backup) sin necesitar conocer de
/// antemano cada <c>tenantId</c>.
/// </remarks>
public interface IDedicatedTenantDatabaseCatalog
{
    /// <summary>
    /// Devuelve el identificador de todos los tenants que hoy tienen una base de datos dedicada
    /// (estrategia T3). El orden no está garantizado; un consumidor que necesite determinismo en el
    /// orden de procesamiento (por ejemplo, para reportar progreso) debe ordenar el resultado.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetDedicatedTenantIdsAsync(CancellationToken cancellationToken);
}
