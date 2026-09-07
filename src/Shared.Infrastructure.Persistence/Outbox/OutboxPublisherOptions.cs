namespace BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;

/// <summary>
/// Configuración del relay de Outbox (F3-03, <c>OutboxBatchProcessor</c>/<c>OutboxPublisherBackgroundService</c>).
/// Se registra como singleton por <c>OutboxPublisherServiceCollectionExtensions.AddSharedOutboxPublisher</c>.
/// </summary>
public sealed class OutboxPublisherOptions
{
    /// <summary>Cuántas filas <c>OutboxMessage</c> pendientes reclama como máximo cada ciclo de sondeo.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Intervalo entre ciclos de sondeo cuando el último lote quedó por debajo de <see cref="BatchSize"/>
    /// (es decir, no hay evidencia de que quede más trabajo pendiente). Cuando un lote sale completo
    /// (<see cref="BatchSize"/> filas reclamadas), <c>OutboxPublisherBackgroundService</c> vuelve a
    /// sondear de inmediato en vez de esperar este intervalo, para drenar más rápido un backlog grande.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Cuánto tiempo queda "reclamada" (<c>OutboxMessage.LockedUntilUtc</c>) una fila por esta
    /// instancia antes de que otra instancia del worker pueda volver a reclamarla si esta murió sin
    /// llegar a marcarla como procesada ni a liberar el lock explícitamente. Debe ser sensiblemente
    /// mayor al tiempo esperado de un ciclo de publicación de un lote completo (publicar + marcar);
    /// un valor demasiado corto arriesga que dos instancias procesen la misma fila si la primera
    /// todavía sigue viva pero lenta, un valor demasiado largo retrasa la recuperación tras una caída.
    /// </summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Identificador de esta instancia del worker (solo diagnóstico/observabilidad, ver
    /// <c>OutboxMessage.LockedBy</c>). Por defecto, un valor único por proceso (nombre de máquina +
    /// GUID) — no participa de la lógica de bloqueo en sí, que es puramente por tiempo
    /// (<see cref="LockDuration"/>) más <c>UPDLOCK, READPAST</c> a nivel de fila.
    /// </summary>
    public string WorkerId { get; set; } = $"{Environment.MachineName}#{Guid.NewGuid():N}";
}
