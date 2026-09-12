using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Domain.Inbox;

/// <summary>
/// Registro persistido de un mensaje de integración RECIBIDO (F1-24, Inbox base): lado receptor,
/// contraparte de <c>OutboxMessage</c> (F1-23, lado emisor). <see cref="MessageId"/> es el
/// identificador único que trae el mensaje/evento original (por ejemplo, la clave de partición +
/// offset de Kafka, o cualquier identificador de negocio único que el productor incluya) — es lo que
/// <c>InboxMessageProcessor</c> (Shared.Application) usa para detectar que un mensaje ya fue procesado
/// y descartar el reintento sin volver a ejecutar el handler ("at-least-once delivery": un broker como
/// Kafka puede entregar el mismo mensaje más de una vez).
/// </summary>
/// <remarks>
/// Esta tarea (F1-24) cubre únicamente el MECANISMO genérico de deduplicación — no existe todavía
/// ningún consumidor real de Kafka (Fase 3, ADR <c>docs/adr/0005-mensajeria-kafka.md</c>, todavía
/// "Proposed") que produzca estas filas a partir de un broker externo. Es análogo a lo que F1-23 hizo
/// con <c>OutboxMessage</c>: preparar la infraestructura antes de que el broker exista, para que un
/// futuro consumidor real solo tenga que invocar <c>IInboxMessageProcessor.ProcessAsync</c> por cada
/// mensaje recibido, sin reinventar la deduplicación.
///
/// <see cref="ProcessedAtUtc"/> solo se establece cuando el <c>handler</c> del mensaje se ejecutó con
/// éxito, en el MISMO <c>SaveChangesAsync</c> que persiste el efecto de negocio de ese handler (mismo
/// patrón de atomicidad que <c>OutboxSaveChangesInterceptor</c>/<c>IdempotencyBehavior</c>). Si el
/// handler lanza una excepción, <c>InboxMessageProcessor</c> nunca llega a llamar
/// <c>SaveChangesAsync</c>, así que NINGUNA fila queda persistida para ese intento — a los fines de la
/// base de datos, el mensaje nunca se procesó, y un reintento posterior con el mismo
/// <see cref="MessageId"/> vuelve a ejecutar el handler con normalidad (no se lo trata como duplicado).
/// Esto es lo que distingue un "duplicado real" (fila existente con <see cref="ProcessedAtUtc"/> no
/// nulo) de un "intento fallido que debe reintentarse" (ninguna fila sobrevive un handler que lanzó
/// excepción). <see cref="RetryCount"/> y <see cref="Error"/> quedan reservados para un futuro
/// consumidor que decida persistir explícitamente sus intentos fallidos (por ejemplo, para una cola de
/// mensajes muertos/DLQ) — <c>InboxMessageProcessor</c> (F1-24) no los escribe desde ningún camino de
/// fallo, con el mismo criterio que <c>OutboxMessage.RetryCount</c>/<c>Error</c> en F1-23.
///
/// Implementa <see cref="ITenantEntity"/> con el mismo criterio que <c>OutboxMessage</c>/
/// <c>IdempotencyKey</c> (regla dura 7 de <c>docs/convenciones.md</c>): el índice único
/// (<c>TenantId</c>, <see cref="MessageId"/>) de <c>InboxModelConfigurator</c> evita que dos tenants
/// distintos colisionen si el mismo identificador de mensaje literal se reutiliza entre ellos.
/// </remarks>
public sealed class InboxMessage : ITenantEntity
{
    public Guid Id { get; init; }

    public Guid TenantId { get; set; }

    /// <summary>Identificador único del mensaje original (broker/evento de integración) — clave de deduplicación.</summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Nombre lógico o calificado del tipo de mensaje/evento de integración recibido, para poder
    /// interpretar <see cref="PayloadJson"/> sin depender de una tabla de mapeo adicional.
    /// </summary>
    public required string MessageType { get; init; }

    /// <summary>JSON del mensaje recibido, tal como llegó del broker/productor.</summary>
    public required string PayloadJson { get; init; }

    public DateTime ReceivedAtUtc { get; init; }

    /// <summary>
    /// <see langword="null"/> mientras el mensaje no fue procesado con éxito. Una vez establecido,
    /// esta fila representa un duplicado real: <c>InboxMessageProcessor</c> descarta cualquier
    /// reintento posterior con el mismo <see cref="MessageId"/> sin volver a ejecutar el handler.
    /// </summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>Reservado para un futuro consumidor que persista intentos fallidos explícitamente (Fase 3); F1-24 no lo incrementa.</summary>
    public int RetryCount { get; set; }

    /// <summary>Reservado para un futuro consumidor (Fase 3, por ejemplo una DLQ); F1-24 no lo establece.</summary>
    public string? Error { get; set; }
}
