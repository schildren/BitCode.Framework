namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Fuente de la asignación explícita tenant → shard usada por <c>TenantShardMapResolver</c>
/// (Shared.Infrastructure.Persistence, prototipo F1-13). Se modela como un mapeo explícito y
/// auditable (en vez de, por ejemplo, un hash consistente del <c>tenantId</c> módulo N shards)
/// porque el repositorio ya cuenta con SQL Server como store de control (ADR 0003) y un mapeo
/// explícito no sufre el problema de reubicación conocido de <c>hash mod N</c>: agregar un shard
/// nuevo (incrementar N) recalcula el resto de los tenants con un hash módulo, mientras que agregar
/// una fila nueva a una tabla de mapeo no toca ninguna fila existente. La contrapartida es que un
/// mapeo explícito requiere una operación de asignación (quién decide a qué shard va cada tenant
/// nuevo) en vez de resolverse "solo"; se considera aceptable porque mover un tenant grande a un
/// shard dedicado es, en la práctica, una decisión operativa deliberada, no un evento automático de
/// alta frecuencia.
/// </summary>
public interface ITenantShardMapStore
{
    /// <summary>
    /// Devuelve el <see cref="ShardId"/> asignado explícitamente a <paramref name="tenantId"/>, o
    /// <see langword="null"/> si no tiene una asignación explícita (en cuyo caso el tenant permanece
    /// en <see cref="ShardId.Shared"/> — T1 es siempre el destino por defecto).
    /// </summary>
    Task<ShardId?> TryGetShardAsync(Guid tenantId, CancellationToken cancellationToken);
}
