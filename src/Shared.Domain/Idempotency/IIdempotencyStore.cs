namespace BitCode.Framework.Shared.Domain.Idempotency;

/// <summary>
/// Store de idempotencia (F1-22): mismo patrón que <c>IRepository</c>/<c>IUnitOfWork</c>, contrato en
/// Shared.Domain implementado en Shared.Infrastructure.Persistence sobre el mismo <c>DbContext</c> de
/// scope. <see cref="Add"/>/<see cref="Remove"/> son deliberadamente síncronos (como
/// <c>IRepository.Update</c>/<c>Remove</c>): solo modifican el <c>ChangeTracker</c> en memoria, la
/// persistencia real ocurre en el mismo <c>IUnitOfWork.SaveChangesAsync</c> que ya confirma el efecto
/// del comando (ver <c>IdempotencyBehavior</c>, Shared.Application) — nunca hacen su propio
/// <c>SaveChanges</c>.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Busca una entrada por <paramref name="key"/>. No recibe un <c>TenantId</c> explícito: el
    /// filtro global de tenancy de EF Core (F1-12) ya restringe la búsqueda al tenant del scope
    /// actual, porque <see cref="IdempotencyKey"/> implementa <c>ITenantEntity</c>.
    /// </summary>
    Task<IdempotencyKey?> FindAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Marca <paramref name="record"/> para insertar en el próximo <c>SaveChangesAsync</c>.</summary>
    void Add(IdempotencyKey record);

    /// <summary>
    /// Marca <paramref name="record"/> para eliminar en el próximo <c>SaveChangesAsync</c> (F1-22:
    /// usado para descartar una entrada vencida antes de insertar una nueva con la misma clave).
    /// </summary>
    void Remove(IdempotencyKey record);

    /// <summary>
    /// Elimina en bloque (ejecución inmediata contra la base, no diferida a <c>SaveChangesAsync</c>)
    /// todas las entradas cuyo <c>ExpiresAtUtc</c> ya pasó, de cualquier tenant. Pensado para
    /// invocarse desde un job de mantenimiento periódico (F1-22, expiración) — ver
    /// docs/convenciones.md para cómo cablearlo con <c>AddSharedBackgroundJobs</c>.
    /// </summary>
    Task<int> PurgeExpiredAsync(DateTime utcNow, CancellationToken cancellationToken = default);
}
