using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Notifications.Envio;

/// <summary>
/// Tracking (Fase 6, módulo 8: "tracking" del Plan Maestro) append-only de CADA intento de entrega de
/// una <see cref="Notification"/> -- distinto del estado agregado de <see cref="Notification"/> (que
/// solo refleja el ÚLTIMO intento): esta tabla es el historial completo, mismo espíritu que
/// <c>WorkflowHistorial</c> (Fase 6, módulo 6) frente al estado agregado de <c>WorkflowInstance</c>.
/// Nunca se actualiza ni se borra una fila ya escrita.
/// </summary>
public sealed class NotificationDelivery : Entity<Guid>, ITenantEntity
{
    public Guid NotificationId { get; private set; }

    public NotificationChannel Canal { get; private set; }

    /// <summary>1-based: el intento original cuenta como intento número 1, no 0.</summary>
    public int IntentoNumero { get; private set; }

    public DateTime TimestampUtc { get; private set; }

    public NotificationDeliveryResultado Resultado { get; private set; }

    public string? ErrorMensaje { get; private set; }

    public Guid TenantId { get; set; }

    public NotificationDelivery(
        Guid id, Guid notificationId, NotificationChannel canal, int intentoNumero,
        NotificationDeliveryResultado resultado, string? errorMensaje)
        : base(id)
    {
        NotificationId = notificationId;
        Canal = canal;
        IntentoNumero = intentoNumero;
        TimestampUtc = DateTime.UtcNow;
        Resultado = resultado;
        ErrorMensaje = errorMensaje;
    }

    private NotificationDelivery()
    {
    }
}
