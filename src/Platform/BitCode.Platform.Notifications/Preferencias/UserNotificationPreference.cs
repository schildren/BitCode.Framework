using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Notifications.Preferencias;

/// <summary>
/// Opt-out de un usuario para un tipo de notificación + canal (Fase 6, módulo 8: "preferencias" del
/// Plan Maestro). La EXISTENCIA de una fila para (<see cref="UserId"/>, <see cref="CodigoPlantilla"/>,
/// <see cref="Canal"/>) significa "este usuario no quiere recibir este tipo de notificación por este
/// canal" -- no hay una columna <c>booleana</c> "OptedOut" separada: menos estado que pueda quedar
/// inconsistente (fila presente pero con el booleano en false, por ejemplo). Opt-in (volver a recibir)
/// es simplemente borrar la fila.
/// </summary>
public sealed class UserNotificationPreference : Entity<Guid>, ITenantEntity, IAuditedEntity
{
    public Guid UserId { get; private set; }

    public string CodigoPlantilla { get; private set; } = string.Empty;

    public NotificationChannel Canal { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public UserNotificationPreference(Guid id, Guid userId, string codigoPlantilla, NotificationChannel canal)
        : base(id)
    {
        UserId = userId;
        CodigoPlantilla = codigoPlantilla;
        Canal = canal;
    }

    private UserNotificationPreference()
    {
    }
}
