namespace BitCode.Framework.Platform.Notifications;

/// <summary>Catálogo de permisos RBAC (F2-07) de este módulo -- ver <c>docs/guia-notifications.md</c>
/// para la matriz permiso/endpoint. "Ver notificación"/"marcar como leída"/"ver preferencias propias"
/// exigen solo el permiso genérico correspondiente; el control fino de "solo la propia" es una
/// verificación de ownership en el handler (mismo criterio que Task Inbox/Workflow), no una regla ABAC
/// configurable.</summary>
public static class NotificationsPermissions
{
    public const string PlantillasAdministrar = "notifications.plantillas.administrar";
    public const string NotificacionesEnviar = "notifications.notificaciones.enviar";
    public const string NotificacionesVer = "notifications.notificaciones.ver";
    public const string NotificacionesMarcarLeida = "notifications.notificaciones.marcarleida";
    public const string PreferenciasAdministrar = "notifications.preferencias.administrar";
}
