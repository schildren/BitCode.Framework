namespace BitCode.Framework.Platform.Catalogs;

/// <summary>
/// Catálogo de permisos RBAC (F2-07, convención <c>"{entidad}.{accion}"</c>) que este módulo exige
/// declarativamente en sus endpoints -- ver <c>docs/guia-catalogs.md</c> para la matriz completa
/// permiso/endpoint/ABAC. Un consumidor real otorga estos permisos a sus roles con
/// <c>RoleManagerPermissionExtensions.AddPermissionAsync</c> (F2-09, reutilizado, no reimplementado).
/// </summary>
public static class CatalogsPermissions
{
    public const string CatalogosVer = "catalogos.catalogos.ver";
    public const string CatalogosCrear = "catalogos.catalogos.crear";

    public const string VersionesCrear = "catalogos.versiones.crear";

    /// <summary>
    /// Permiso distinto y deliberadamente más restrictivo que <see cref="VersionesCrear"/> (requisito
    /// común de Fase 6: "publicar una nueva versión de un catálogo que otros módulos consumen es más
    /// sensible que solo leerlo/crear un borrador") -- además de este permiso RBAC, la operación pasa
    /// por una regla ABAC de alcance (<c>AttributeScopeAbacRule</c>, F2-08) sobre el atributo
    /// <c>catalogoId</c>, ver <c>PublicarCatalogoVersionCommandHandler</c> y la sección "RBAC + ABAC" de
    /// la guía.
    /// </summary>
    public const string VersionesPublicar = "catalogos.versiones.publicar";

    public const string ParametrosVer = "catalogos.parametros.ver";
    public const string ParametrosCrear = "catalogos.parametros.crear";

    public const string VigenciasVer = "catalogos.parametros.vigencias.ver";
    public const string VigenciasCrear = "catalogos.parametros.vigencias.crear";
}
