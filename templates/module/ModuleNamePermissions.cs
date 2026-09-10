namespace MyApp.Modules;

/// <summary>
/// Catálogo de permisos RBAC de este módulo (ver docs/convenciones.md, "Zero Trust por defecto" --
/// default deny, permiso explícito). Cada módulo declara sus propios permisos; ningún módulo reutiliza
/// ni referencia la constante de permiso de otro módulo -- si dos módulos necesitan compartir una
/// política de autorización, eso es una decisión de composición del host, no un acoplamiento de
/// biblioteca a biblioteca.
/// </summary>
public static class ModuleNamePermissions
{
    /// <summary>Listar/ver el detalle de un elemento del módulo.</summary>
    public const string ElementosVer = "modulename.elementos.ver";

    /// <summary>Crear un elemento nuevo en el módulo.</summary>
    public const string ElementosAdministrar = "modulename.elementos.administrar";
}
