using BitCode.Framework.Shared.Domain.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Idempotency;

/// <summary>
/// Implementación EF Core de <see cref="IIdempotencyStore"/> (F1-22): comparte el mismo
/// <see cref="DbContext"/> de scope que <c>IUnitOfWork</c>/<c>IRepository</c> — <see cref="Add"/> y
/// <see cref="Remove"/> solo modifican el <c>ChangeTracker</c> (igual que
/// <c>RepositoryBase.Update</c>/<c>Remove</c>); la persistencia real ocurre en el mismo
/// <c>SaveChangesAsync</c> que ya confirma el efecto del comando (ver <c>IdempotencyBehavior</c>,
/// registrado dentro del alcance de <c>TransactionBehavior</c> en el pipeline de MediatR) — así un
/// fallo a mitad de camino nunca deja el registro de idempotencia sin el efecto real, ni al revés.
/// </summary>
public class EfIdempotencyStore(DbContext dbContext) : IIdempotencyStore
{
    public Task<IdempotencyKey?> FindAsync(string key, CancellationToken cancellationToken = default) =>
        dbContext.Set<IdempotencyKey>().FirstOrDefaultAsync(record => record.Key == key, cancellationToken);

    public void Add(IdempotencyKey record) => dbContext.Set<IdempotencyKey>().Add(record);

    public void Remove(IdempotencyKey record) => dbContext.Set<IdempotencyKey>().Remove(record);

    /// <summary>
    /// Elimina en bloque las entradas vencidas (F1-22, expiración): pensado para invocarse desde un
    /// job de mantenimiento periódico (p. ej. un <c>IJob</c> de Quartz.NET registrado vía
    /// <c>AddSharedBackgroundJobs</c>, ver docs/convenciones.md) — esta tarea no incluye scheduling
    /// automático. Usa <c>IgnoreQueryFilters</c> porque la limpieza debe alcanzar las entradas de
    /// todos los tenants, no solo el tenant del scope actual, y <c>ExecuteDeleteAsync</c> (borrado en
    /// bloque de EF Core) para no traer las filas a memoria antes de eliminarlas.
    /// </summary>
    public Task<int> PurgeExpiredAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        dbContext.Set<IdempotencyKey>()
            .IgnoreQueryFilters()
            .Where(record => record.ExpiresAtUtc <= utcNow)
            .ExecuteDeleteAsync(cancellationToken);
}
