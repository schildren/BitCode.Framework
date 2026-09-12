namespace BitCode.Framework.Platform.IntegrationHub;

/// <summary>Catálogo de permisos RBAC (F2-07) de este módulo -- ver <c>docs/guia-integration-hub.md</c>
/// para la matriz permiso/endpoint. A diferencia de Notifications/Task Inbox, ninguna operación de este
/// módulo tiene un control de ownership adicional además del permiso (ver el <c>remarks</c> de
/// <c>Solicitudes.ObtenerSolicitudQuery</c>): son datos operacionales de integración entre sistemas, no
/// datos personales de un usuario final.</summary>
public static class IntegrationHubPermissions
{
    public const string ConectoresAdministrar = "integrationhub.conectores.administrar";
    public const string ConectoresVer = "integrationhub.conectores.ver";
    public const string SolicitudesEnviar = "integrationhub.solicitudes.enviar";
    public const string SolicitudesVer = "integrationhub.solicitudes.ver";
}
