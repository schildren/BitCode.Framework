namespace BitCode.Framework.Platform.TaskInbox;

/// <summary>Catálogo de permisos RBAC (F2-07) de este módulo -- ver <c>docs/guia-taskinbox.md</c> para
/// la matriz permiso/endpoint. "Marcar como leída" exige solo <see cref="BandejaMarcarLeida"/>; el
/// control fino de "no puedo marcar como leída la tarea de otro" es una verificación de ownership en
/// el handler (mismo criterio que Workflow, ver <c>docs/guia-workflow.md</c>, sección "RBAC y
/// ownership"), no una regla ABAC configurable.</summary>
public static class TaskInboxPermissions
{
    public const string BandejaVer = "taskinbox.bandeja.ver";
    public const string BandejaMarcarLeida = "taskinbox.bandeja.marcarleida";
}
