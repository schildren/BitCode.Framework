namespace BitCode.Framework.Shared.Domain.Inbox;

/// <summary>
/// Store de Inbox (F1-24): mismo patrón que <c>IIdempotencyStore</c> (F1-22) — contrato en
/// Shared.Domain implementado en Shared.Infrastructure.Persistence sobre el mismo <c>DbContext</c> de
/// scope. <see cref="Add"/> es deliberadamente síncrono: solo modifica el <c>ChangeTracker</c> en
/// memoria, la persistencia real ocurre en el mismo <c>IUnitOfWork.SaveChangesAsync</c> que
/// <c>InboxMessageProcessor</c> (Shared.Application) ya usa para confirmar el efecto del handler del
/// mensaje — nunca hace su propio <c>SaveChanges</c>.
/// </summary>
public interface IInboxStore
{
    /// <summary>
    /// Busca una entrada por <paramref name="messageId"/>. No recibe un <c>TenantId</c> explícito: el
    /// filtro global de tenancy de EF Core (F1-12) ya restringe la búsqueda al tenant del scope
    /// actual, porque <see cref="InboxMessage"/> implementa <c>ITenantEntity</c>.
    /// </summary>
    Task<InboxMessage?> FindAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>Marca <paramref name="record"/> para insertar en el próximo <c>SaveChangesAsync</c>.</summary>
    void Add(InboxMessage record);

    /// <summary>
    /// Deja de trackear <paramref name="record"/> sin persistir ningún cambio (F1-24: usado por
    /// <c>InboxMessageProcessor</c> cuando el handler de un mensaje lanza una excepción, para que el
    /// registro que <see cref="Add"/> había agregado al <c>ChangeTracker</c> no quede "colgado" en
    /// memoria — de lo contrario, un reintento posterior dentro del mismo scope/<c>DbContext</c>
    /// intentaría insertar una segunda fila con el mismo <c>MessageId</c> y violaría el índice único).
    /// </summary>
    void Discard(InboxMessage record);
}
