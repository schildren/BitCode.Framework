namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Contrato de resolución de la cadena de conexión física asociada a un <see cref="ShardId"/>
/// (F1-13, estrategia T2). Separado de <see cref="IShardResolver"/> a propósito: qué shard le
/// corresponde a un tenant (una decisión de negocio/operación, potencialmente cacheable durante
/// mucho tiempo) es una pregunta distinta de a qué servidor/base física apunta ese shard hoy (un
/// detalle de infraestructura que puede cambiar por mantenimiento, failover o reprovisioning sin que
/// el tenant "se mueva" de shard lógico).
/// </summary>
/// <remarks>
/// El framework registra por defecto <c>SingleConnectionStringShardProvider</c>
/// (Shared.Infrastructure.Persistence), que ignora el <see cref="ShardId"/> recibido y devuelve
/// siempre la única cadena de conexión configurada por <c>AddSharedPersistence</c> — es exactamente
/// el comportamiento actual de la estrategia T1 (F1-12), sin cambios. Conectar shards físicos reales
/// (más de un SQL Server, provisioning, migraciones de datos entre shards) es una tarea de
/// infraestructura posterior (F1-14 en adelante o cuando exista un tenant real que lo requiera), no
/// parte de este contrato ni del prototipo de F1-13.
/// </remarks>
public interface IShardConnectionStringProvider
{
    Task<string> GetConnectionStringAsync(ShardId shardId, CancellationToken cancellationToken);
}
