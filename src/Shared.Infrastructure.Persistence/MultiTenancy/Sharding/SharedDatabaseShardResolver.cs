using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;

/// <summary>
/// Implementación por defecto de <see cref="IShardResolver"/> (F1-13): resuelve todo tenant a
/// <see cref="ShardId.Shared"/>, preservando exactamente el comportamiento de la estrategia T1
/// (F1-12) — base de datos compartida, sin sharding. <c>AddSharedPersistence</c> la registra con
/// <c>TryAddScoped</c> para que un proyecto que no optó explícitamente por T2 no note ningún cambio.
/// </summary>
public sealed class SharedDatabaseShardResolver : IShardResolver
{
    public Task<ShardId> ResolveShardAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(ShardId.Shared);
}
