namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Identificador opaco y determinístico de un shard (grupo de tenants que comparten la misma
/// infraestructura física de base de datos) para la estrategia T2 (F1-13, routing a shards por
/// grupos de tenants). No es una cadena de conexión: <see cref="IShardConnectionStringProvider"/>
/// es quien traduce un <see cref="ShardId"/> a la infraestructura real de ese shard.
/// </summary>
/// <param name="Value">
/// Clave estable del shard (p. ej. <c>"shard-01"</c>). Debe ser determinística y no debe cambiar
/// una vez asignada a un tenant: renombrar un <see cref="ShardId"/> existente equivale a mover
/// todos sus tenants a un shard "nuevo" desde la perspectiva de cualquier caché o índice que lo use
/// como clave.
/// </param>
public readonly record struct ShardId(string Value)
{
    /// <summary>
    /// Shard reservado para tenants en estrategia T1 (base de datos compartida, F1-12/ADR 0003):
    /// todo tenant resuelto a este <see cref="ShardId"/> sigue usando exactamente la misma conexión y
    /// el mismo <c>MultiTenantDbContext</c> que hoy, sin ningún cambio de comportamiento observable.
    /// Un <c>IShardResolver</c> de T2 (F1-13) nunca debe reasignar a otro shard un tenant que ya
    /// resuelve aquí sin una migración de datos explícita — eso es responsabilidad de una tarea de
    /// implementación de infraestructura posterior (fuera del alcance de F1-13, que es solo diseño y
    /// prototipo), no de este contrato.
    /// </summary>
    public static readonly ShardId Shared = new("t1-shared");

    public override string ToString() => Value;
}
