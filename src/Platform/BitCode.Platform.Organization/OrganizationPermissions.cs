namespace BitCode.Framework.Platform.Organization;

/// <summary>
/// Catálogo de permisos RBAC (F2-07, convención <c>"{entidad}.{accion}"</c>) que este módulo exige
/// declarativamente en sus endpoints -- ver <c>docs/guia-organization.md</c> para la matriz completa
/// permiso/endpoint/ABAC. Un consumidor real otorga estos permisos a sus roles con
/// <c>RoleManagerPermissionExtensions.AddPermissionAsync</c> (F2-09, reutilizado, no reimplementado).
/// </summary>
public static class OrganizationPermissions
{
    public const string EmpresasVer = "organizacion.empresas.ver";
    public const string EmpresasCrear = "organizacion.empresas.crear";

    /// <summary>
    /// Permiso distinto y deliberadamente más restrictivo que <see cref="EmpresasCrear"/> y que
    /// <see cref="SucursalesCrear"/> (requisito común de Fase 6: "desactivar una empresa completa
    /// requiere un permiso distinto y más restrictivo que crear una sucursal") -- además de este
    /// permiso RBAC, la operación pasa por una regla ABAC de alcance (<c>AttributeScopeAbacRule</c>,
    /// F2-08) sobre el atributo <c>empresaId</c>, ver <c>DesactivarEmpresaCommandHandler</c> y la
    /// sección "RBAC + ABAC" de la guía.
    /// </summary>
    public const string EmpresasDesactivar = "organizacion.empresas.desactivar";

    public const string SucursalesVer = "organizacion.sucursales.ver";
    public const string SucursalesCrear = "organizacion.sucursales.crear";
    public const string SucursalesDesactivar = "organizacion.sucursales.desactivar";

    public const string AreasVer = "organizacion.areas.ver";
    public const string AreasCrear = "organizacion.areas.crear";

    public const string CargosVer = "organizacion.cargos.ver";
    public const string CargosCrear = "organizacion.cargos.crear";
}
