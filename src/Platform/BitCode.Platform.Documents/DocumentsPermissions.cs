namespace BitCode.Framework.Platform.Documents;

/// <summary>
/// Catálogo de permisos RBAC (F2-07, convención <c>"{entidad}.{accion}"</c>) que este módulo exige
/// declarativamente en sus endpoints -- ver <c>docs/guia-documents.md</c> para la matriz completa
/// permiso/endpoint/ABAC. Un consumidor real otorga estos permisos a sus roles con
/// <c>RoleManagerPermissionExtensions.AddPermissionAsync</c> (F2-09, reutilizado, no reimplementado).
/// </summary>
public static class DocumentsPermissions
{
    public const string DocumentosVer = "documents.documentos.ver";

    public const string DocumentosCrear = "documents.documentos.crear";

    /// <summary>Permiso distinto de <see cref="DocumentosCrear"/> -- subir una versión nueva sobre un
    /// documento existente es una operación separada (Épica de Documents: "Versionado"), evaluada además
    /// por la misma regla ABAC de alcance que <see cref="DocumentosDescargar"/>/<see cref="DocumentosDisponer"/>
    /// (ver <c>SubirVersionDocumentoCommandHandler</c>).</summary>
    public const string DocumentosSubirVersion = "documents.documentos.subirversion";

    /// <summary>Operación sensible de referencia del módulo (checklist Fase 6, requisito común 8
    /// "Auditoría de operaciones críticas" -- "sobre todo la descarga de un documento sensible es un
    /// evento auditable"): además de este permiso RBAC, la operación pasa por una regla ABAC de alcance
    /// (<c>AttributeScopeAbacRule</c>, F2-08) sobre el atributo <c>documentoId</c>, ver
    /// <c>DescargarDocumentoVersionQueryHandler</c>.</summary>
    public const string DocumentosDescargar = "documents.documentos.descargar";

    /// <summary>Misma sensibilidad y misma regla ABAC que <see cref="DocumentosDescargar"/> -- ver
    /// <c>DisponerDocumentoCommandHandler</c>.</summary>
    public const string DocumentosDisponer = "documents.documentos.disponer";
}
