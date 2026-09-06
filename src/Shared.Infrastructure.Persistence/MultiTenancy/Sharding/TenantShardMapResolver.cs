using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;

/// <summary>
/// Prototipo de <see cref="IShardResolver"/> para la estrategia T2 (F1-13): resuelve el shard de un
/// tenant a partir de una asignación explícita en <see cref="ITenantShardMapStore"/>. Si el tenant no
/// tiene una fila de asignación, resuelve a <see cref="ShardId.Shared"/> (T1) — es decir, "sin
/// asignación explícita" nunca significa "shard indeterminado" ni introduce aleatoriedad: significa,
/// de forma determinística, "sigue en la base de datos compartida".
/// </summary>
/// <remarks>
/// Este resolver es <b>determinístico</b> siempre que <see cref="ITenantShardMapStore"/> no reasigne
/// una fila existente: la resolución de un <c>tenantId</c> dado depende únicamente del valor
/// almacenado para ese <c>tenantId</c>, nunca del orden de resolución, del número de tenants ya
/// resueltos, ni de ningún otro estado mutable. A diferencia de un hash consistente del
/// <c>tenantId</c> módulo N shards (alternativa considerada y descartada para el prototipo — ver
/// ADR 0010), agregar una fila de asignación nueva para un tenant nuevo no reubica ningún tenant ya
/// asignado, porque cada fila es independiente; un hash módulo N, en cambio, recalcula la asignación
/// de todos los tenants existentes cada vez que N cambia (agregar o quitar un shard), lo que exigiría
/// una migración de datos coordinada de múltiples tenants a la vez.
/// </remarks>
public sealed class TenantShardMapResolver(ITenantShardMapStore shardMapStore) : IShardResolver
{
    public async Task<ShardId> ResolveShardAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var explicitShard = await shardMapStore.TryGetShardAsync(tenantId, cancellationToken);
        return explicitShard ?? ShardId.Shared;
    }
}
