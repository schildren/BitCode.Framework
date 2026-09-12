using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;

namespace BitCode.Framework.Platform.Notifications.Actors;

/// <summary>
/// Resuelve "quién" ejecuta la operación actual del módulo Notifications -- mismo patrón que
/// <c>ITaskInboxActorContext</c> (<see cref="GetCurrentUserId"/>, ownership) y
/// <c>IDocumentsActorContext</c> (<see cref="GetCurrentActor"/>, auditoría F2-15) combinados, porque este
/// módulo necesita ambas cosas: ownership para "mis notificaciones"/"mis preferencias" y auditoría para
/// el alta de una <c>NotificationTemplate</c> (una decisión de configuración con valor de cumplimiento).
/// </summary>
public interface INotificationsActorContext
{
    /// <summary><see langword="null"/> fuera de un pipeline HTTP autenticado.</summary>
    Guid? GetCurrentUserId();

    /// <summary>Actor para auditoría (F2-15) -- <see cref="AuditActorType.User"/> con el
    /// <c>NameIdentifier</c> del request autenticado, o <see cref="AuditActorType.System"/> ("system")
    /// si no hay <c>HttpContext</c>/usuario autenticado (por ejemplo, <c>NotificationRetryJob</c>, que no
    /// corre dentro de un pipeline HTTP).</summary>
    AuditActor GetCurrentActor();

    ClaimsPrincipal? GetCurrentPrincipal();
}
