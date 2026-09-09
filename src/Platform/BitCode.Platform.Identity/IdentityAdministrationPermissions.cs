namespace BitCode.Framework.Platform.Identity;

/// <summary>
/// Catálogo de permisos RBAC (F2-07, convención <c>"{entidad}.{accion}"</c>) que este módulo exige
/// declarativamente (<c>[RequirePermission]</c>) en sus endpoints -- ver
/// <c>docs/guia-identity-administration.md</c> para la matriz completa permiso/endpoint/ABAC. Un
/// consumidor real otorga estos permisos a sus roles con
/// <c>RoleManagerPermissionExtensions.AddPermissionAsync</c> (F2-09, reutilizado, no reimplementado).
/// </summary>
public static class IdentityAdministrationPermissions
{
    public const string UsuariosVer = "identidad.usuarios.ver";
    public const string UsuariosCrear = "identidad.usuarios.crear";
    public const string UsuariosDesactivar = "identidad.usuarios.desactivar";

    /// <summary>
    /// ALCANCE GLOBAL, no por-tenant -- <c>ApplicationRole</c> (Security 2.0, Fase 2) no implementa
    /// <c>ITenantEntity</c> (a diferencia de <c>ApplicationUser</c>, que sí lo hace); es una decisión
    /// ya tomada en la fase de Security 2.0, no introducida por este módulo. <see cref="RolesVer"/>,
    /// <see cref="RolesCrear"/> y <see cref="RolesPermisosAdministrar"/> operan sobre el catálogo de
    /// roles COMPLETO del sistema, visible y mutable por cualquier actor al que se le otorguen --
    /// un tenant con estos permisos ve los roles (nombres y permisos concedidos) de TODOS los
    /// tenants, y puede mutar un rol que otros tenants ya usan. Un consumidor real de este módulo
    /// NUNCA debe otorgar estos tres permisos a un rol de administración de tenant regular -- están
    /// pensados para un actor de plataforma/superadministrador global. Ver
    /// <c>docs/guia-identity-administration.md</c>, sección "Riesgos conocidos", para el detalle y la
    /// alternativa (ADR pendiente: roles por-tenant requeriría agregar <c>TenantId</c> a
    /// <c>ApplicationRole</c> en Security 2.0).
    /// </summary>
    public const string RolesVer = "identidad.roles.ver";

    /// <inheritdoc cref="RolesVer"/>
    public const string RolesCrear = "identidad.roles.crear";

    /// <inheritdoc cref="RolesVer"/>
    public const string RolesPermisosAdministrar = "identidad.roles.permisos.administrar";

    /// <summary>
    /// Permiso RBAC distinto de <see cref="UsuariosVer"/> a propósito -- ver requisito común de Fase 6
    /// "asignar un rol de administrador requiere un permiso distinto al de ver el listado de
    /// usuarios". Además de este permiso base, la asignación de roles pasa por la regla ABAC
    /// <c>SelfRoleAssignmentAbacRule</c> (no-autoasignación).
    /// </summary>
    public const string UsuariosRolesAsignar = "identidad.usuarios.roles.asignar";

    public const string SesionesVer = "identidad.sesiones.ver";
    public const string SesionesRevocar = "identidad.sesiones.revocar";
}
