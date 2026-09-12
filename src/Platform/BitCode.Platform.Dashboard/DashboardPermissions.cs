namespace BitCode.Framework.Platform.Dashboard;

/// <summary>Catálogo de permisos RBAC (F2-07) de este módulo -- ver <c>docs/guia-dashboard.md</c> para la
/// matriz permiso/endpoint. Mismo criterio que Task Inbox (Fase 6, módulo 7): el permiso genérico solo
/// habilita ver/administrar el PROPIO dashboard -- el control fino de "no puedo ver/tocar el dashboard de
/// otro usuario" es una verificación de ownership en cada handler (ver <c>Widgets/ObtenerWidgetQuery</c>),
/// no una regla ABAC configurable.</summary>
public static class DashboardPermissions
{
    /// <summary>Listar/ver el detalle de un widget propio y resolver su métrica.</summary>
    public const string WidgetsVer = "dashboard.widgets.ver";

    /// <summary>Agregar, quitar y reordenar widgets del propio dashboard.</summary>
    public const string WidgetsAdministrar = "dashboard.widgets.administrar";
}
