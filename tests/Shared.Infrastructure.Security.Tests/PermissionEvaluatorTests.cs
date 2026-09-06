using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

public class PermissionEvaluatorTests
{
    private static ITenantContext CreateTenantContext(Guid? tenantId, bool isMultiTenancyEnabled)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsMultiTenancyEnabled.Returns(isMultiTenancyEnabled);
        tenantContext.TenantId.Returns(tenantId);
        return tenantContext;
    }

    private static ClaimsPrincipal CreateAuthenticatedUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));

    [Fact]
    public async Task EvaluateAsync_UnauthenticatedPrincipal_ReturnsEmpty()
    {
        var permissionService = Substitute.For<IPermissionService>();
        var tenantContext = CreateTenantContext(null, isMultiTenancyEnabled: false);
        var evaluator = new PermissionEvaluator(permissionService, tenantContext);
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await evaluator.EvaluateAsync(anonymousUser);

        result.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_UserIdRecognized_ExpandsPermissionsFromLocalIdentityRoles()
    {
        var userId = Guid.NewGuid();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(["productos.crear"]);
        var tenantContext = CreateTenantContext(null, isMultiTenancyEnabled: false);
        var evaluator = new PermissionEvaluator(permissionService, tenantContext);
        var user = CreateAuthenticatedUser(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        var result = await evaluator.EvaluateAsync(user);

        result.HasPermission("productos.crear").Should().BeTrue();
        result.Grants.Should().ContainSingle(g => g.Source == PermissionGrantSources.LocalIdentityRoles);
    }

    [Fact]
    public async Task EvaluateAsync_NoLocalUserId_ExpandsPermissionsFromRoleClaimByName()
    {
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>())
            .Returns(["productos.editar"]);
        var tenantContext = CreateTenantContext(null, isMultiTenancyEnabled: false);
        var evaluator = new PermissionEvaluator(permissionService, tenantContext);
        var user = CreateAuthenticatedUser(new Claim(ClaimTypes.Role, "Editor"));

        var result = await evaluator.EvaluateAsync(user);

        result.HasPermission("productos.editar").Should().BeTrue();
        result.Grants.Should().ContainSingle(g => g.Source == PermissionGrantSources.FromRoleClaim("Editor"));
        await permissionService.DidNotReceive().GetPermissionsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_TokenPermissionClaim_IsGrantedDirectlyWithoutLocalIdentity()
    {
        var permissionService = Substitute.For<IPermissionService>();
        var tenantContext = CreateTenantContext(null, isMultiTenancyEnabled: false);
        var evaluator = new PermissionEvaluator(permissionService, tenantContext);
        var user = CreateAuthenticatedUser(new Claim(PermissionClaimTypes.Permission, "pedidos.reservar-stock"));

        var result = await evaluator.EvaluateAsync(user);

        result.HasPermission("pedidos.reservar-stock").Should().BeTrue();
        result.Grants.Should().ContainSingle(g => g.Source == PermissionGrantSources.TokenPermissionClaim);
    }

    [Fact]
    public async Task EvaluateAsync_ScopeClaimWithPermissionShape_NarrowsEffectivePermissions()
    {
        var userId = Guid.NewGuid();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(["productos.crear", "productos.eliminar"]);
        var tenantContext = CreateTenantContext(null, isMultiTenancyEnabled: false);
        var evaluator = new PermissionEvaluator(permissionService, tenantContext);
        var user = CreateAuthenticatedUser(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ScopeClaimTypes.Scope, "productos.crear"));

        var result = await evaluator.EvaluateAsync(user);

        result.HasPermission("productos.crear").Should().BeTrue();
        result.HasPermission("productos.eliminar").Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_ScopeClaimWithoutPermissionShape_DoesNotNarrow()
    {
        var userId = Guid.NewGuid();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(["productos.crear"]);
        var tenantContext = CreateTenantContext(null, isMultiTenancyEnabled: false);
        var evaluator = new PermissionEvaluator(permissionService, tenantContext);
        var user = CreateAuthenticatedUser(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ScopeClaimTypes.Scope, "openid profile email"));

        var result = await evaluator.EvaluateAsync(user);

        result.HasPermission("productos.crear").Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_TenantClaimMismatchesResolvedTenant_ReturnsEmpty()
    {
        var userId = Guid.NewGuid();
        var resolvedTenantId = Guid.NewGuid();
        var tokenTenantId = Guid.NewGuid();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(["productos.crear"]);
        var tenantContext = CreateTenantContext(resolvedTenantId, isMultiTenancyEnabled: true);
        var evaluator = new PermissionEvaluator(permissionService, tenantContext);
        var user = CreateAuthenticatedUser(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(TenantClaimTypes.TenantId, tokenTenantId.ToString()));

        var result = await evaluator.EvaluateAsync(user);

        result.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_TenantClaimMatchesResolvedTenant_EvaluatesNormally()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(["productos.crear"]);
        var tenantContext = CreateTenantContext(tenantId, isMultiTenancyEnabled: true);
        var evaluator = new PermissionEvaluator(permissionService, tenantContext);
        var user = CreateAuthenticatedUser(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(TenantClaimTypes.TenantId, tenantId.ToString()));

        var result = await evaluator.EvaluateAsync(user);

        result.HasPermission("productos.crear").Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_MultiTenancyEnabledWithoutTenantClaimOnToken_DoesNotBlock()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(["productos.crear"]);
        var tenantContext = CreateTenantContext(tenantId, isMultiTenancyEnabled: true);
        var evaluator = new PermissionEvaluator(permissionService, tenantContext);
        var user = CreateAuthenticatedUser(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        var result = await evaluator.EvaluateAsync(user);

        result.HasPermission("productos.crear").Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_NameIdentifierIsGuidShapedButUnknownLocally_FallsBackToRoleClaimExpansion()
    {
        // Bugfix F2-07: el "sub" de un IdP externo (Keycloak incluido) es casi siempre un UUID, que
        // JwtSecurityTokenHandler mapea automáticamente a ClaimTypes.NameIdentifier -- antes de este
        // fix, CUALQUIER identidad puramente externa con un "sub" con forma de GUID tomaba por error la
        // rama "hay userId local" (Guid.TryParse tiene éxito), consultaba un IPermissionService que
        // nunca la reconoce (NullPermissionService en un proyecto solo-OIDC) y JAMÁS llegaba a expandir
        // sus roles por nombre, aunque ClaimTypes.Role estuviera presente.
        var unrecognizedExternalSubject = Guid.NewGuid();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(unrecognizedExternalSubject, Arg.Any<CancellationToken>())
            .Returns([]);
        permissionService.GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>())
            .Returns(["productos.editar"]);
        var tenantContext = CreateTenantContext(null, isMultiTenancyEnabled: false);
        var evaluator = new PermissionEvaluator(permissionService, tenantContext);
        var user = CreateAuthenticatedUser(
            new Claim(ClaimTypes.NameIdentifier, unrecognizedExternalSubject.ToString()),
            new Claim(ClaimTypes.Role, "Editor"));

        var result = await evaluator.EvaluateAsync(user);

        result.HasPermission("productos.editar").Should().BeTrue();
        result.Grants.Should().ContainSingle(g => g.Source == PermissionGrantSources.FromRoleClaim("Editor"));
    }

    [Fact]
    public async Task EvaluateAsync_AfterOidcRoleClaimsTransformation_ExpandsRolesFromKeycloakShapedRealmAccessClaim()
    {
        // Reproduce el bug real de F2-07: sin OidcRoleClaimsTransformation corriendo antes (como lo
        // hace el pipeline real de autenticación, vía IClaimsTransformation), un token de Keycloak solo
        // trae "realm_access" (JSON anidado) -- nunca ClaimTypes.Role -- y este evaluador debía devolver
        // EffectivePermissions.Empty pese a que Keycloak sí tiene el rol asignado. Este test ejercita
        // ambas piezas juntas, a partir de la forma real del claim (no un ClaimTypes.Role ya armado a
        // mano, ver PermissionEvaluatorTests.EvaluateAsync_NoLocalUserId_ExpandsPermissionsFromRoleClaimByName).
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForRoleAsync("Editor", Arg.Any<CancellationToken>())
            .Returns(["productos.editar"]);
        var tenantContext = CreateTenantContext(null, isMultiTenancyEnabled: false);
        var evaluator = new PermissionEvaluator(permissionService, tenantContext);

        var oidcOptions = Options.Create(new OidcOptions
        {
            Authority = "https://keycloak.local/realms/bitcode",
            Audience = "bitcode-api",
        });
        var claimsTransformation = new OidcRoleClaimsTransformation(oidcOptions);
        var rawKeycloakPrincipal = CreateAuthenticatedUser(
            new Claim("realm_access", """{"roles":["Editor"]}"""));

        var principalComoLoVeriaElPipelineDeAutenticacion =
            await claimsTransformation.TransformAsync(rawKeycloakPrincipal);
        var result = await evaluator.EvaluateAsync(principalComoLoVeriaElPipelineDeAutenticacion);

        result.HasPermission("productos.editar").Should().BeTrue();
        result.Grants.Should().ContainSingle(g => g.Source == PermissionGrantSources.FromRoleClaim("Editor"));
    }
}
