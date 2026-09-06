namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Contrato de resolución de shard (F1-13, estrategia T2 — routing a shards por grupos de tenants).
/// La resolución debe ser <b>determinística</b>: para el mismo <paramref name="tenantId"/>, dos
/// invocaciones cualesquiera (misma instancia, distintas instancias, distintos momentos) deben
/// devolver siempre el mismo <see cref="ShardId"/> mientras no exista una migración explícita del
/// tenant a otro shard. Ninguna implementación puede introducir aleatoriedad ni depender de estado
/// mutable no versionado (p. ej. el orden de inserción en una colección en memoria que puede variar
/// entre reinicios).
/// </summary>
/// <remarks>
/// El framework registra por defecto <c>SharedDatabaseShardResolver</c> (Shared.Infrastructure.Persistence),
/// que devuelve siempre <see cref="ShardId.Shared"/> — preserva el comportamiento de la estrategia T1
/// (F1-12) sin cambios para cualquier proyecto que no haya optado explícitamente por T2. Un proyecto
/// que necesite enrutar un subconjunto de tenants a shards separados reemplaza este registro por una
/// implementación como <c>TenantShardMapResolver</c> (prototipo F1-13), que solo debe redirigir a un
/// <see cref="ShardId"/> distinto de <see cref="ShardId.Shared"/> a los tenants explícitamente
/// asignados, dejando a todos los demás en T1.
/// </remarks>
public interface IShardResolver
{
    /// <summary>
    /// Resuelve de forma determinística el <see cref="ShardId"/> al que pertenece
    /// <paramref name="tenantId"/>.
    /// </summary>
    Task<ShardId> ResolveShardAsync(Guid tenantId, CancellationToken cancellationToken);
}
