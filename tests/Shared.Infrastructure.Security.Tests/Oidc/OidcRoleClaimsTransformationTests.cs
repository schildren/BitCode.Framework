using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc;

/// <summary>
/// F2-07 (bugfix de correctitud): estos tests construyen el claim superior con la MISMA forma que
/// realmente emite <c>JwtSecurityTokenHandler</c> para un token de Keycloak -- un único claim
/// (<c>"realm_access"</c>) cuyo valor es el JSON crudo del objeto anidado, nunca un
/// <see cref="ClaimTypes.Role"/> ya en su forma final. <see cref="PermissionEvaluatorTests"/> arma el
/// <see cref="ClaimTypes.Role"/> a mano y por eso nunca hubiera detectado que este paso previo faltaba.
/// </summary>
public class OidcRoleClaimsTransformationTests
{
    private static OidcRoleClaimsTransformation CreateTransformation(params string[] roleClaimJsonPaths)
    {
        var options = new OidcOptions
        {
            Authority = "https://keycloak.local/realms/bitcode",
            Audience = "bitcode-api",
        };

        if (roleClaimJsonPaths.Length > 0)
        {
            options.RoleClaimJsonPaths = roleClaimJsonPaths;
        }

        return new OidcRoleClaimsTransformation(Options.Create(options));
    }

    private static ClaimsPrincipal CreateAuthenticatedPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Bearer"));

    [Fact]
    public async Task TransformAsync_RealmAccessRolesClaim_ProjectsEachRoleAsClaimTypesRole()
    {
        var transformation = CreateTransformation();
        var principal = CreateAuthenticatedPrincipal(
            new Claim("realm_access", """{"roles":["Editor","Viewer"]}"""));

        var transformed = await transformation.TransformAsync(principal);

        transformed.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Should().BeEquivalentTo("Editor", "Viewer");
    }

    [Fact]
    public async Task TransformAsync_NoRealmAccessClaim_DoesNotAddAnyRoleClaim()
    {
        var transformation = CreateTransformation();
        var principal = CreateAuthenticatedPrincipal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        var transformed = await transformation.TransformAsync(principal);

        transformed.FindAll(ClaimTypes.Role).Should().BeEmpty();
    }

    [Fact]
    public async Task TransformAsync_RealmAccessClaimWithoutRolesProperty_DoesNotAddAnyRoleClaim()
    {
        var transformation = CreateTransformation();
        var principal = CreateAuthenticatedPrincipal(
            new Claim("realm_access", """{"otraCosa":"valor"}"""));

        var transformed = await transformation.TransformAsync(principal);

        transformed.FindAll(ClaimTypes.Role).Should().BeEmpty();
    }

    [Fact]
    public async Task TransformAsync_RealmAccessClaimWithInvalidJson_DoesNotThrowAndDoesNotAddRoleClaim()
    {
        var transformation = CreateTransformation();
        var principal = CreateAuthenticatedPrincipal(
            new Claim("realm_access", "no-es-json"));

        var act = () => transformation.TransformAsync(principal);

        (await act.Should().NotThrowAsync()).Which
            .FindAll(ClaimTypes.Role).Should().BeEmpty();
    }

    [Fact]
    public async Task TransformAsync_AlreadyHasMatchingRoleClaim_DoesNotDuplicateIt()
    {
        var transformation = CreateTransformation();
        var principal = CreateAuthenticatedPrincipal(
            new Claim("realm_access", """{"roles":["Editor"]}"""),
            new Claim(ClaimTypes.Role, "Editor"));

        var transformed = await transformation.TransformAsync(principal);

        transformed.FindAll(ClaimTypes.Role).Should().ContainSingle(c => c.Value == "Editor");
    }

    [Fact]
    public async Task TransformAsync_ResourceAccessClientRolesPathConfigured_ProjectsClientRoles()
    {
        // Camino de configuración para roles de cliente Keycloak (resource_access.<client-id>.roles) --
        // el nombre de cliente concreto se agrega vía configuración, no vive hardcodeado en el código
        // del framework (ADR 0004).
        var transformation = CreateTransformation("realm_access.roles", "resource_access.bitcode-api.roles");
        var principal = CreateAuthenticatedPrincipal(
            new Claim("realm_access", """{"roles":["Editor"]}"""),
            new Claim("resource_access", """{"bitcode-api":{"roles":["ClientAdmin"]}}"""));

        var transformed = await transformation.TransformAsync(principal);

        transformed.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Should().BeEquivalentTo("Editor", "ClientAdmin");
    }

    [Fact]
    public async Task TransformAsync_UnauthenticatedPrincipal_IsReturnedUnchanged()
    {
        var transformation = CreateTransformation();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var transformed = await transformation.TransformAsync(principal);

        transformed.Should().BeSameAs(principal);
        transformed.FindAll(ClaimTypes.Role).Should().BeEmpty();
    }
}
