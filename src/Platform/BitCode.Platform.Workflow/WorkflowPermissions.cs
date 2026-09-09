namespace BitCode.Framework.Platform.Workflow;

/// <summary>
/// Catálogo de permisos RBAC (F2-07, convención <c>"{entidad}.{accion}"</c>) que este módulo exige
/// declarativamente en sus endpoints -- ver <c>docs/guia-workflow.md</c> para la matriz completa
/// permiso/endpoint. Un consumidor real otorga estos permisos a sus roles con
/// <c>RoleManagerPermissionExtensions.AddPermissionAsync</c> (F2-09, reutilizado, no reimplementado). La
/// operación "resolver mi propia tarea asignada" NO requiere un permiso RBAC de alcance amplio adicional
/// al permiso base <see cref="TareasResolver"/> -- el control fino de "no puedo resolver la tarea de
/// otro" es una verificación de ownership explícita en el handler (comparar
/// <c>WorkflowTask.AsignadoAUserId</c> contra el actor autenticado), no una regla ABAC configurable
/// (ver <c>ResolverTareaCommandHandler</c> y <c>docs/guia-workflow.md</c>, sección "RBAC y ownership").
/// </summary>
public static class WorkflowPermissions
{
    public const string DefinicionesVer = "workflow.definiciones.ver";
    public const string DefinicionesCrear = "workflow.definiciones.crear";

    public const string VersionesCrear = "workflow.versiones.crear";
    public const string VersionesPublicar = "workflow.versiones.publicar";

    public const string InstanciasIniciar = "workflow.instancias.iniciar";
    public const string InstanciasVer = "workflow.instancias.ver";

    public const string TareasVer = "workflow.tareas.ver";
    public const string TareasResolver = "workflow.tareas.resolver";
    public const string TareasDelegar = "workflow.tareas.delegar";
}
