namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Resultado de <see cref="IPermissionEvaluator.EvaluateAsync"/> (F2-07, RBAC 2.0): el conjunto de
/// permisos que la identidad actual puede ejercer, cada uno con su <see cref="PermissionGrant.Source"/>
/// (trazabilidad). Default deny: un principal sin permisos calculados es <see cref="Empty"/>, nunca
/// <see langword="null"/>.
/// </summary>
public sealed class EffectivePermissions(IReadOnlyCollection<PermissionGrant> grants)
{
    public static EffectivePermissions Empty { get; } = new([]);

    public IReadOnlyCollection<PermissionGrant> Grants { get; } = grants;

    public bool HasPermission(string permission) =>
        Grants.Any(g => string.Equals(g.Permission, permission, StringComparison.Ordinal));

    /// <summary>
    /// Conjunto de permisos sin duplicados, sin el detalle de origen — para el caso de uso que solo
    /// necesita "¿qué puede hacer?" y no "¿por qué puede hacerlo?" (ver <see cref="Grants"/> para lo
    /// segundo).
    /// </summary>
    public IReadOnlyCollection<string> AsPermissionSet() =>
        Grants.Select(g => g.Permission).Distinct(StringComparer.Ordinal).ToArray();
}
