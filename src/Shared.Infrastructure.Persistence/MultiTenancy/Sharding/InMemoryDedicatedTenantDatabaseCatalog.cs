using System.Collections.Concurrent;
using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;

/// <summary>
/// Implementación de referencia de <see cref="IDedicatedTenantDatabaseCatalog"/> para el prototipo
/// de F1-14: mantiene el conjunto de tenants dedicados (T3) en memoria del proceso.
/// </summary>
/// <remarks>
/// <b>No apta para producción multi-instancia</b>, por el mismo motivo que
/// <see cref="InMemoryTenantShardMapStore"/> (ADR 0010): cada instancia tendría su propia copia y un
/// reinicio la pierde. Una implementación productiva real respaldaría este catálogo con la misma
/// tabla de control en SQL Server que respalde <see cref="ITenantShardMapStore"/> en producción (por
/// ejemplo, "todo tenant cuyo <c>ShardId</c> asignado no sea compartido por ningún otro tenant es un
/// tenant T3"), tarea de implementación posterior fuera del alcance de F1-14 (ver ADR 0011).
/// </remarks>
public sealed class InMemoryDedicatedTenantDatabaseCatalog : IDedicatedTenantDatabaseCatalog
{
    private readonly ConcurrentDictionary<Guid, byte> _dedicatedTenantIds = new();

    /// <summary>
    /// Registra <paramref name="tenantId"/> como tenant con base de datos dedicada. Idempotente:
    /// registrar el mismo tenant más de una vez no tiene efecto adicional.
    /// </summary>
    public void Register(Guid tenantId) => _dedicatedTenantIds.TryAdd(tenantId, 0);

    public Task<IReadOnlyCollection<Guid>> GetDedicatedTenantIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Guid>>(_dedicatedTenantIds.Keys.ToArray());
}
