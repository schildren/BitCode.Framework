using System.Collections.Concurrent;
using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;

/// <summary>
/// Implementación de referencia de <see cref="ITenantShardMapStore"/> para el prototipo de F1-13:
/// mantiene la asignación tenant → shard en un diccionario en memoria del proceso.
/// </summary>
/// <remarks>
/// <b>No apta para producción multi-instancia</b>: cada instancia del proceso tendría su propia copia
/// del mapeo, y un reinicio la pierde por completo. Sirve para (a) demostrar y probar de forma
/// determinística el contrato <see cref="ITenantShardMapStore"/>/<see cref="TenantShardMapResolver"/>
/// sin depender de infraestructura adicional, y (b) como base de referencia rápida para tests o
/// entornos de un solo proceso (demos, desarrollo local). La implementación productiva equivalente
/// (una tabla de control en SQL Server, ya disponible en el store de control del framework —
/// ver ADR 0003/0010) es una tarea de implementación posterior, fuera del alcance de F1-13, que pide
/// explícitamente "contratos y prototipo".
/// </remarks>
public sealed class InMemoryTenantShardMapStore : ITenantShardMapStore
{
    private readonly ConcurrentDictionary<Guid, ShardId> _assignments = new();

    /// <summary>
    /// Asigna explícitamente <paramref name="tenantId"/> a <paramref name="shardId"/>. Idempotente:
    /// reasignar el mismo tenant al mismo shard no tiene efecto adicional; reasignarlo a un shard
    /// distinto sobrescribe la asignación anterior (una migración real de datos entre shards es
    /// responsabilidad de quien orqueste esa migración, no de este store).
    /// </summary>
    public void Assign(Guid tenantId, ShardId shardId) => _assignments[tenantId] = shardId;

    public Task<ShardId?> TryGetShardAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<ShardId?>(_assignments.TryGetValue(tenantId, out var shardId) ? shardId : null);
}
