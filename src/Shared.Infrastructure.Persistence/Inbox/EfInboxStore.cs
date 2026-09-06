using BitCode.Framework.Shared.Domain.Inbox;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Inbox;

/// <summary>
/// Implementación EF Core de <see cref="IInboxStore"/> (F1-24): comparte el mismo <see cref="DbContext"/>
/// de scope que <c>IUnitOfWork</c>/<c>IRepository</c> — <see cref="Add"/> solo modifica el
/// <c>ChangeTracker</c> (igual que <c>EfIdempotencyStore.Add</c>, F1-22); la persistencia real ocurre en
/// el mismo <c>SaveChangesAsync</c> que <c>InboxMessageProcessor</c> (Shared.Application) ya usa para
/// confirmar el efecto del handler del mensaje.
/// </summary>
public class EfInboxStore(DbContext dbContext) : IInboxStore
{
    public Task<InboxMessage?> FindAsync(string messageId, CancellationToken cancellationToken = default) =>
        dbContext.Set<InboxMessage>().FirstOrDefaultAsync(record => record.MessageId == messageId, cancellationToken);

    public void Add(InboxMessage record) => dbContext.Set<InboxMessage>().Add(record);

    /// <summary>
    /// <c>EntityState.Detached</c> funciona tanto para una entidad recién agregada (<c>Added</c>,
    /// nunca llegó a insertarse en la base) como para una ya trackeada desde una consulta previa
    /// (<c>Unchanged</c>/<c>Modified</c>): en ambos casos, deja de existir en el <c>ChangeTracker</c>
    /// sin emitir ningún comando SQL, así que el próximo <c>FindAsync</c> vuelve a leer el estado real
    /// de la base de datos (que nunca cambió, porque <c>InboxMessageProcessor</c> descarta el registro
    /// antes de haber llamado a <c>SaveChangesAsync</c>).
    /// </summary>
    public void Discard(InboxMessage record) => dbContext.Entry(record).State = EntityState.Detached;
}
