namespace BitCode.Framework.Shared.Application.Inbox;

/// <summary>Resultado de <see cref="IInboxMessageProcessor.ProcessAsync"/> (F1-24).</summary>
public enum InboxProcessOutcome
{
    /// <summary>El mensaje era nuevo (o un reintento legítimo tras un fallo previo): el handler se ejecutó y su efecto quedó persistido junto con la marca de procesado.</summary>
    Processed,

    /// <summary>El mensaje ya había sido procesado con éxito antes: duplicado real, descartado sin ejecutar el handler.</summary>
    Discarded,
}

/// <summary>
/// Mecanismo genérico de Inbox (F1-24): dado un identificador único de mensaje (<paramref name="messageId"/>
/// de <see cref="ProcessAsync"/>, el que traería el broker/productor original), detecta si ya fue
/// procesado con éxito antes de ejecutar <c>handler</c> — así un mensaje entregado más de una vez
/// ("at-least-once delivery", el caso típico de un broker como Kafka) no duplica su efecto de negocio.
/// </summary>
/// <remarks>
/// Pensado para ser invocado por un futuro consumidor real de mensajería (Fase 3, ADR
/// <c>docs/adr/0005-mensajeria-kafka.md</c>, todavía "Proposed") una vez por cada mensaje recibido del
/// broker, ANTES de reconocer/hacer commit del offset ante el broker: solo si <see cref="ProcessAsync"/>
/// devuelve sin lanzar excepción (ya sea <see cref="InboxProcessOutcome.Processed"/> o
/// <see cref="InboxProcessOutcome.Discarded"/>) es seguro avanzar el offset. Esta tarea (F1-24) no
/// implementa ningún consumidor real ni ninguna suscripción a un broker — solo el mecanismo genérico,
/// reutilizable independientemente del formato de mensaje concreto.
/// </remarks>
public interface IInboxMessageProcessor
{
    /// <summary>
    /// Procesa <paramref name="messageId"/> de forma deduplicada. Si ya existe un registro de Inbox
    /// para este <paramref name="messageId"/> marcado como procesado, descarta la llamada sin invocar
    /// <paramref name="handler"/>. Caso contrario (mensaje nuevo, o un reintento legítimo de un
    /// intento anterior que lanzó una excepción y por lo tanto nunca llegó a persistirse), ejecuta
    /// <paramref name="handler"/> y, solo si termina con éxito, persiste el efecto de negocio que
    /// <paramref name="handler"/> haya dejado en el <c>DbContext</c> de scope junto con la marca de
    /// procesado del mensaje, en un único <c>SaveChangesAsync</c> atómico.
    /// </summary>
    /// <param name="messageId">Identificador único del mensaje original — clave de deduplicación.</param>
    /// <param name="messageType">Nombre lógico del tipo de mensaje, solo para trazabilidad/diagnóstico.</param>
    /// <param name="payload">Cuerpo del mensaje tal como llegó del broker/productor, persistido tal cual.</param>
    /// <param name="handler">
    /// Efecto de negocio a ejecutar para este mensaje. Debe modificar el estado a través del mismo
    /// <c>DbContext</c>/<c>IUnitOfWork</c> de scope (por ejemplo, vía <c>IRepository</c>) sin llamar a
    /// <c>SaveChangesAsync</c> por su cuenta — <see cref="ProcessAsync"/> es quien decide cuándo
    /// confirmar, igual que <c>OutboxSaveChangesInterceptor</c>/<c>IdempotencyBehavior</c> con el
    /// <c>ChangeTracker</c> del comando en curso.
    /// </param>
    Task<InboxProcessOutcome> ProcessAsync(
        string messageId,
        string messageType,
        string payload,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);
}
