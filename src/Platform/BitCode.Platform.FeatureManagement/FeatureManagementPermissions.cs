namespace BitCode.Framework.Platform.FeatureManagement;

/// <summary>
/// Catálogo de permisos RBAC (F2-07, convención <c>"{entidad}.{accion}"</c>) que este módulo exige
/// declarativamente en sus endpoints -- ver <c>docs/guia-feature-management.md</c> para la matriz
/// completa permiso/endpoint/ABAC. Un consumidor real otorga estos permisos a sus roles con
/// <c>RoleManagerPermissionExtensions.AddPermissionAsync</c> (F2-09, reutilizado, no reimplementado).
/// </summary>
public static class FeatureManagementPermissions
{
    public const string FlagsVer = "featuremanagement.flags.ver";
    public const string FlagsCrear = "featuremanagement.flags.crear";

    /// <summary>Permiso distinto y deliberadamente más restrictivo que <see cref="FlagsCrear"/>
    /// (requisito común de Fase 6: "activar un flag que puede cambiar el comportamiento de producción
    /// para usuarios reales es más sensible que solo crearlo apagado") -- además de este permiso RBAC, la
    /// operación pasa por una regla ABAC de alcance (<c>AttributeScopeAbacRule</c>, F2-08) sobre el
    /// atributo <c>featureFlagId</c>, ver <c>ActivarFeatureFlagCommandHandler</c> y la sección
    /// "RBAC + ABAC" de la guía.</summary>
    public const string FlagsActivar = "featuremanagement.flags.activar";

    /// <summary>Misma sensibilidad y misma regla ABAC que <see cref="FlagsActivar"/> -- ver
    /// <c>DesactivarFeatureFlagCommandHandler</c>.</summary>
    public const string FlagsDesactivar = "featuremanagement.flags.desactivar";

    public const string SegmentosVer = "featuremanagement.segmentos.ver";
    public const string SegmentosCrear = "featuremanagement.segmentos.crear";

    public const string RolloutsVer = "featuremanagement.rollouts.ver";
    public const string RolloutsCrear = "featuremanagement.rollouts.crear";

    /// <summary>Permiso separado de <see cref="FlagsVer"/>: evaluar si un flag está activo PARA UN
    /// CONTEXTO dado (tenant/usuario/claims) es la operación que consumen aplicaciones cliente en
    /// runtime, con un volumen de llamadas muy superior a la administración -- separarlo permite otorgar
    /// este permiso a un actor de servicio (aplicación cliente) sin darle acceso al CRUD administrativo.</summary>
    public const string FlagsEvaluar = "featuremanagement.flags.evaluar";
}
