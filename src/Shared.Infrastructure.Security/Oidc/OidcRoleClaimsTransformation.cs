using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc;

/// <summary>
/// Proyecta como <see cref="ClaimTypes.Role"/> los nombres de rol que un proveedor OIDC entrega
/// anidados dentro de un claim con forma de objeto/array JSON en vez de como un claim plano de string
/// (F2-07, bugfix de correctitud). Necesario porque <c>JwtBearerHandler</c> no aplana claims anidados:
/// un token de Keycloak (el único IdP con el que este repo integra y prueba, ADR 0004) declara los
/// roles de realm como <c>realm_access: { "roles": [...] }</c> -- <c>JwtSecurityTokenHandler</c> deja
/// ese contenido como un único claim <c>"realm_access"</c> cuyo valor es el JSON crudo, nunca como
/// <c>ClaimTypes.Role</c>. Sin esta transformación, <c>PermissionEvaluator</c>
/// (<see cref="Permissions.PermissionEvaluator"/>) nunca encuentra <see cref="ClaimTypes.Role"/> para
/// una identidad puramente externa (sin <c>ApplicationUser</c> local) y devuelve
/// <c>EffectivePermissions.Empty</c> pase lo que pase en el IdP.
/// </summary>
/// <remarks>
/// Deliberadamente agnóstica del nombre del claim/ruta concreta (<see cref="OidcOptions.RoleClaimJsonPaths"/>,
/// configurable) para no acoplar el adapter a un proveedor: cada ruta es
/// <c>"claimSuperior.propiedad[.propiedadAnidada...]"</c>, resuelta genéricamente navegando el JSON del
/// claim superior -- no hay ningún nombre de cliente/"resource" de Keycloak hardcodeado en el código.
/// El default (<c>"realm_access.roles"</c>) cubre el caso real verificado con Keycloak (roles de
/// realm); un proyecto que necesite también roles de cliente puede agregar
/// <c>"resource_access.&lt;client-id&gt;.roles"</c> a <see cref="OidcOptions.RoleClaimJsonPaths"/> por
/// configuración, sin tocar este código. Cualquier otro IdP OIDC que exponga los roles bajo una forma
/// distinta (por ejemplo un claim plano "roles", o un namespace propio como hace Auth0) también se
/// resuelve por configuración: <see cref="OidcOptions.RoleClaimJsonPaths"/> es una lista, no un único
/// valor fijo.
/// </remarks>
public sealed class OidcRoleClaimsTransformation(IOptions<OidcOptions> oidcOptions) : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        foreach (var path in oidcOptions.Value.RoleClaimJsonPaths)
        {
            AddRoleClaimsFromJsonPath(identity, path);
        }

        return Task.FromResult(principal);
    }

    private static void AddRoleClaimsFromJsonPath(ClaimsIdentity identity, string path)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            // Una ruta sin al menos "claim.propiedad" no identifica ningún array de roles anidado --
            // se ignora en vez de lanzar, para que una entrada de configuración inválida no tumbe la
            // autenticación de toda la identidad.
            return;
        }

        var rootClaim = identity.FindFirst(segments[0]);
        if (rootClaim is null)
        {
            return;
        }

        using var document = TryParseJson(rootClaim.Value);
        if (document is null)
        {
            return;
        }

        var current = document.RootElement;
        for (var i = 1; i < segments.Length; i++)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segments[i], out var next))
            {
                return;
            }

            current = next;
        }

        if (current.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var roleElement in current.EnumerateArray())
        {
            if (roleElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var roleName = roleElement.GetString();
            if (string.IsNullOrWhiteSpace(roleName) || identity.HasClaim(ClaimTypes.Role, roleName))
            {
                continue;
            }

            identity.AddClaim(new Claim(ClaimTypes.Role, roleName, ClaimValueTypes.String, rootClaim.Issuer));
        }
    }

    private static JsonDocument? TryParseJson(string value)
    {
        try
        {
            return JsonDocument.Parse(value);
        }
        catch (JsonException)
        {
            // El claim superior existe pero no es JSON válido (por ejemplo, un IdP que sí emite
            // "realm_access" como claim plano de string) -- no hay array de roles que extraer.
            return null;
        }
    }
}
