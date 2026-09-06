using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Implementación por defecto de <see cref="IPermissionEvaluator"/> (F2-07, RBAC 2.0). Normaliza tres
/// fuentes de permisos en un único resultado trazable:
/// <list type="number">
/// <item><description>Roles de Identity local del <c>ApplicationUser</c> asociado al claim
/// <see cref="ClaimTypes.NameIdentifier"/> (comportamiento heredado de F1, vía
/// <see cref="IPermissionService.GetPermissionsForUserAsync"/>).</description></item>
/// <item><description>Si la fuente anterior NO produjo ningún permiso -- sin userId local reconocible
/// (identidad puramente externa, F2-01, sin <c>ApplicationUser</c> asociado), O con un userId
/// reconocible que <see cref="IPermissionService.GetPermissionsForUserAsync"/> no supo resolver --, los
/// nombres de rol presentes como claim (<see cref="ClaimTypes.Role"/>) se expanden contra los roles
/// conocidos localmente (<see cref="IPermissionService.GetPermissionsForRoleAsync"/>) — así un IdP
/// externo que emita nombres de rol ya reconocidos por el framework también puede autorizar por
/// permiso, sin depender de que el sujeto tenga una fila en la tabla de usuarios local. El fallback se
/// activa por "sin resultado", NO por "sin claim NameIdentifier", porque
/// <see cref="ClaimTypes.NameIdentifier"/> no es un indicador confiable de "hay un ApplicationUser
/// local": <c>JwtSecurityTokenHandler</c> mapea automáticamente el claim estándar <c>"sub"</c> del
/// token a <see cref="ClaimTypes.NameIdentifier"/>, y el <c>sub</c> de la inmensa mayoría de los IdP
/// OIDC (Keycloak incluido) es un UUID -- exactamente la forma que <c>Guid.TryParse</c> acepta. Antes
/// de este bugfix (F2-07), CUALQUIER identidad puramente externa con un <c>sub</c> con forma de GUID
/// tomaba por error la rama "hay userId local", consultaba <c>NullPermissionService</c> (vacío por
/// diseño) y JAMÁS llegaba a expandir sus roles por nombre, sin importar que
/// <see cref="ClaimTypes.Role"/> estuviera presente. Este evaluador SOLO lee
/// <see cref="ClaimTypes.Role"/> ya presente en el <see cref="ClaimsPrincipal"/>: para un token de
/// Keycloak (F2-01, ADR 0004) ese claim no llega así por defecto (los roles de realm vienen anidados
/// como <c>realm_access.roles</c>, un array JSON dentro de un único claim, sin aplanar) — es
/// <c>Oidc.OidcRoleClaimsTransformation</c>, registrada por
/// <c>OidcAuthenticationServiceCollectionExtensions.AddSharedOidcAuthentication</c>, la que proyecta
/// esos roles anidados como <see cref="ClaimTypes.Role"/> ANTES de que el pipeline de autorización (y
/// por lo tanto este evaluador) vea el principal. Sin esa transformación registrada, este camino nunca
/// encuentra nada que expandir para un token puramente OIDC (ver <c>docs/guia-rbac-2.md</c>).</description></item>
/// <item><description>Permisos declarados directamente como claim del token
/// (<see cref="PermissionClaimTypes.Permission"/>) — un IdP externo puede emitir el permiso
/// literal sin pasar por ningún concepto de rol.</description></item>
/// </list>
/// Sobre el resultado combinado se aplican dos límites de seguridad, ambos fail-closed:
/// <list type="bullet">
/// <item><description><b>Tenancy:</b> si el proyecto opera en modo multi-tenant y el token trae un
/// claim <see cref="TenantClaimTypes.TenantId"/> que no coincide con el tenant ya resuelto para el
/// request (<see cref="ITenantContext"/>), el resultado es <see cref="EffectivePermissions.Empty"/> —
/// nunca se evalúan permisos con un tenant inconsistente.</description></item>
/// <item><description><b>Scope OAuth2:</b> ver <see cref="ScopeClaimTypes"/> — si el token declara al
/// menos un scope con forma de permiso, esos scopes narrowean (intersectan) el resultado final.</description></item>
/// </list>
/// </summary>
public sealed class PermissionEvaluator(IPermissionService permissionService, ITenantContext tenantContext)
    : IPermissionEvaluator
{
    public async Task<EffectivePermissions> EvaluateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return EffectivePermissions.Empty;
        }

        if (!IsTenantConsistent(principal))
        {
            return EffectivePermissions.Empty;
        }

        var grants = new List<PermissionGrant>();

        var localIdentityGrants = new List<PermissionGrant>();
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is not null && Guid.TryParse(userIdClaim, out var userId))
        {
            var userPermissions = await permissionService.GetPermissionsForUserAsync(userId, cancellationToken);
            localIdentityGrants.AddRange(userPermissions.Select(p => new PermissionGrant(p, PermissionGrantSources.LocalIdentityRoles)));
        }

        if (localIdentityGrants.Count > 0)
        {
            grants.AddRange(localIdentityGrants);
        }
        else
        {
            // Fallback por "sin resultado" (bugfix F2-07, ver el comentario de clase más arriba) --
            // cubre tanto la ausencia total de NameIdentifier como un NameIdentifier con forma de GUID
            // que IPermissionService no reconoce como ApplicationUser local (el caso real de un "sub"
            // de Keycloak). Para Keycloak, ClaimTypes.Role solo existe acá porque
            // Oidc.OidcRoleClaimsTransformation ya lo proyectó a partir de "realm_access.roles" antes
            // de que este evaluador se ejecute.
            var roleNames = principal.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .Distinct(StringComparer.Ordinal);

            foreach (var roleName in roleNames)
            {
                var rolePermissions = await permissionService.GetPermissionsForRoleAsync(roleName, cancellationToken);
                grants.AddRange(rolePermissions.Select(p => new PermissionGrant(p, PermissionGrantSources.FromRoleClaim(roleName))));
            }
        }

        foreach (var claim in principal.FindAll(PermissionClaimTypes.Permission))
        {
            grants.Add(new PermissionGrant(claim.Value, PermissionGrantSources.TokenPermissionClaim));
        }

        return new EffectivePermissions(ApplyScopeNarrowing(principal, grants));
    }

    private bool IsTenantConsistent(ClaimsPrincipal principal)
    {
        if (!tenantContext.IsMultiTenancyEnabled || tenantContext.TenantId is not { } resolvedTenantId)
        {
            return true;
        }

        var tenantClaim = principal.FindFirst(TenantClaimTypes.TenantId)?.Value;
        if (tenantClaim is null)
        {
            // El token no transporta tenant_id (por ejemplo, un IdP externo sin ese claim propio del
            // JWT emitido localmente, F1-12): no hay con qué contrastar, no se bloquea por ausencia.
            return true;
        }

        return Guid.TryParse(tenantClaim, out var tokenTenantId) && tokenTenantId == resolvedTenantId;
    }

    private static IReadOnlyCollection<PermissionGrant> ApplyScopeNarrowing(
        ClaimsPrincipal principal,
        IReadOnlyCollection<PermissionGrant> grants)
    {
        var declaredScopes = principal.FindAll(ScopeClaimTypes.Scope)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        var permissionShapedScopes = declaredScopes
            .Where(s => s.Contains('.', StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        if (permissionShapedScopes.Count == 0)
        {
            return grants;
        }

        return grants.Where(g => permissionShapedScopes.Contains(g.Permission)).ToArray();
    }
}
