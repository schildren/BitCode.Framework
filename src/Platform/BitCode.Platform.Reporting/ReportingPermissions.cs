namespace BitCode.Framework.Platform.Reporting;

/// <summary>Catálogo de permisos RBAC (F2-07) de este módulo -- ver <c>docs/guia-reporting.md</c>, sección
/// "RBAC y ABAC", para la matriz permiso/endpoint y la decisión honesta de por qué este primer corte no
/// agrega ABAC.</summary>
public static class ReportingPermissions
{
    public const string WorkflowInstanciasVer = "reporting.workflowinstancias.ver";
    public const string WorkflowInstanciasExportar = "reporting.workflowinstancias.exportar";
}
