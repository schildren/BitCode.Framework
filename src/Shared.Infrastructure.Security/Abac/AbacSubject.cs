using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// El "Subject" del modelo ABAC (F2-08): la identidad autenticada de la request actual, envuelta junto
/// con sus <see cref="EffectivePermissions"/> ya calculados por <see cref="IPermissionEvaluator"/>
/// (F2-07, RBAC 2.0) -- ABAC no recalcula permisos por su cuenta, los reutiliza como la pieza RBAC del
/// criterio combinado (ver <c>docs/guia-rbac-2.md</c>, sección "Qué NO resuelve F2-07"). Los atributos
/// del sujeto que una regla ABAC necesite (empresa, sucursal, límite de monto, u otro) se leen como
/// claim del <see cref="Principal"/> -- <see cref="GetClaimValues"/> es el único punto de acceso que
/// una <see cref="IAbacRule"/> del framework usa para esto.
/// </summary>
public sealed class AbacSubject(ClaimsPrincipal principal, EffectivePermissions effectivePermissions)
{
    public ClaimsPrincipal Principal { get; } = principal;

    public EffectivePermissions EffectivePermissions { get; } = effectivePermissions;

    /// <summary>
    /// Todos los valores de un claim dado, sin duplicados -- para un atributo del sujeto que puede
    /// tener más de un valor (por ejemplo, un usuario con acceso a varias sucursales).
    /// </summary>
    public IReadOnlyCollection<string> GetClaimValues(string claimType) =>
        Principal.FindAll(claimType).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
}
