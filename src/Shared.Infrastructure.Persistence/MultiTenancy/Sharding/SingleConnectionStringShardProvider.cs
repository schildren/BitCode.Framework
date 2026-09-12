using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Sharding;

/// <summary>
/// Implementación por defecto de <see cref="IShardConnectionStringProvider"/> (F1-13): ignora el
/// <see cref="ShardId"/> recibido y devuelve siempre la única cadena de conexión configurada por
/// <c>AddSharedPersistence</c>. Es exactamente el comportamiento actual de la estrategia T1
/// (F1-12): mientras ningún proyecto conecte un <see cref="ShardId"/> distinto de
/// <see cref="ShardId.Shared"/> a infraestructura física real, esta implementación es suficiente y
/// no requiere ningún cambio para seguir soportando T1 sin regresiones.
/// </summary>
public sealed class SingleConnectionStringShardProvider(string connectionString)
    : IShardConnectionStringProvider
{
    public Task<string> GetConnectionStringAsync(ShardId shardId, CancellationToken cancellationToken) =>
        Task.FromResult(connectionString);
}
