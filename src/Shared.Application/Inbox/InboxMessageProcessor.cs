using BitCode.Framework.Shared.Domain.Inbox;
using BitCode.Framework.Shared.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Shared.Application.Inbox;

/// <summary>
/// Implementación por defecto de <see cref="IInboxMessageProcessor"/> (F1-24). Da comportamiento real
/// al mecanismo de deduplicación: usa <see cref="IInboxStore"/> (Shared.Domain) para verificar/registrar
/// el mensaje, y <see cref="IUnitOfWork"/> para confirmar, en un único <c>SaveChangesAsync</c>, tanto el
/// efecto de negocio de <c>handler</c> como la marca de procesado del mensaje — mismo patrón de
/// atomicidad que <c>OutboxSaveChangesInterceptor</c> (F1-23) e <c>IdempotencyBehavior</c> (F1-22).
/// </summary>
/// <remarks>
/// Cómo se distingue un "duplicado real" de un "intento fallido que debe reintentarse" (el punto más
/// sutil de F1-24): esta clase NUNCA llama a <c>SaveChangesAsync</c> antes de que <c>handler</c>
/// complete con éxito. Si <c>handler</c> lanza una excepción, el registro de <see cref="InboxMessage"/>
/// que esta clase agregó al <c>ChangeTracker</c> (junto con cualquier cambio de negocio parcial que
/// <c>handler</c> haya dejado en el mismo <c>ChangeTracker</c>) nunca llega a persistirse — la
/// excepción se propaga tal cual (regla dura de <c>docs/convenciones.md</c>: nunca tragarse una
/// excepción para simular éxito) y la fila de Inbox queda, a los fines de la base de datos, como si el
/// mensaje nunca se hubiera recibido. Un reintento posterior con el MISMO <c>messageId</c> encuentra
/// <c>existing == null</c> (ninguna fila sobrevivió) y ejecuta <c>handler</c> de nuevo con normalidad —
/// no se lo trata como duplicado. Solo cuando <c>handler</c> termina con éxito y el
/// <c>SaveChangesAsync</c> subsiguiente confirma, la fila queda con <c>ProcessedAtUtc</c> no nulo, y
/// ESA es la única condición que <see cref="ProcessAsync"/> usa para descartar un reintento futuro como
/// duplicado real. Mismo criterio de "solo lo exitoso deja rastro" que <c>IdempotencyBehavior</c>
/// documenta para <c>IdempotencyKey.ResponseValueJson</c>.
///
/// Límite conocido (no cubierto por el criterio de aceptación literal, igual que el "riesgo conocido"
/// documentado por F1-22 para <c>IdempotencyBehavior</c>): dos llamadas concurrentes con el mismo
/// <c>messageId</c> que todavía no persistió ninguna fila (por ejemplo, dos particiones/consumidores
/// entregando el mismo mensaje casi al mismo tiempo) pueden ejecutar <c>handler</c> dos veces en
/// paralelo; el índice único (<c>TenantId</c>, <c>MessageId</c>) de <c>InboxModelConfigurator</c> evita
/// que ambas persistan (la segunda falla al hacer <c>SaveChangesAsync</c> con una violación de índice
/// único), pero esa segunda ejecución no se traduce automáticamente en un "cache hit" — un consumidor
/// real de Fase 3 debe decidir cómo tratar esa excepción (por ejemplo, reintentando el mensaje, lo que
/// esta vez sí encuentra el registro ya marcado como procesado si la primera ejecución ganó la carrera).
/// </remarks>
public class InboxMessageProcessor(
    IInboxStore inboxStore,
    IUnitOfWork unitOfWork,
    ILogger<InboxMessageProcessor> logger)
    : IInboxMessageProcessor
{
    public async Task<InboxProcessOutcome> ProcessAsync(
        string messageId,
        string messageType,
        string payload,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        var existing = await inboxStore.FindAsync(messageId, cancellationToken);

        if (existing is not null && existing.ProcessedAtUtc is not null)
        {
            logger.LogInformation(
                "Mensaje {MessageId} ({MessageType}) ya fue procesado; se descarta sin ejecutar el handler",
                messageId,
                messageType);
            return InboxProcessOutcome.Discarded;
        }

        // existing no nulo con ProcessedAtUtc nulo no puede ocurrir a través del camino normal de esta
        // clase (nunca persiste una fila sin marcarla como procesada, ver remarks de la clase), pero se
        // maneja de forma defensiva por si una fila "recibida" llegó a existir por otra vía: se reusa en
        // vez de violar el índice único (TenantId, MessageId) al intentar insertar una nueva.
        InboxMessage record;

        if (existing is null)
        {
            record = new InboxMessage
            {
                Id = Guid.NewGuid(),
                MessageId = messageId,
                MessageType = messageType,
                PayloadJson = payload,
                ReceivedAtUtc = DateTime.UtcNow,
            };
            inboxStore.Add(record);
        }
        else
        {
            record = existing;
            record.RetryCount++;
            logger.LogInformation(
                "Reintentando mensaje {MessageId} ({MessageType}), intento número {RetryCount}",
                messageId,
                messageType,
                record.RetryCount);
        }

        // Deliberadamente sin SaveChangesAsync antes de esta línea: si handler lanza, la excepción se
        // propaga sin que nada de lo anterior (ni este registro de Inbox ni cualquier cambio de negocio
        // que handler haya dejado en el mismo ChangeTracker) llegue a persistirse en la base de datos.
        // Ver remarks de la clase para el razonamiento completo.
        try
        {
            await handler(cancellationToken);
        }
        catch
        {
            // El registro seguiría trackeado en memoria (Added/Modified) aunque nunca se haya
            // persistido nada: sin este Discard, un reintento posterior DENTRO DEL MISMO scope/
            // DbContext (por ejemplo, el mismo consumidor reintentando el mismo mensaje sin abrir un
            // scope nuevo) chocaría al intentar agregar una segunda fila con el mismo MessageId al
            // mismo ChangeTracker, violando el índice único antes incluso de llegar a SaveChangesAsync.
            inboxStore.Discard(record);
            throw;
        }

        record.ProcessedAtUtc = DateTime.UtcNow;
        record.Error = null;

        // Único SaveChangesAsync: persiste, atómicamente, tanto el efecto de negocio que handler dejó
        // en el ChangeTracker como esta fila de Inbox marcada como procesada.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return InboxProcessOutcome.Processed;
    }
}
