using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Abac;

public class AuthorizationPolicyEvaluatorTests
{
    private static ClaimsPrincipal CreateAuthenticatedUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));

    private static ClaimsPrincipal CreateAnonymousUser() => new(new ClaimsIdentity());

    private static IPermissionEvaluator CreatePermissionEvaluator(EffectivePermissions result)
    {
        var evaluator = Substitute.For<IPermissionEvaluator>();
        evaluator.EvaluateAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(result);
        return evaluator;
    }

    private static EffectivePermissions PermissionsWith(params string[] permissions) =>
        new(permissions.Select(p => new PermissionGrant(p, "test")).ToArray());

    [Fact]
    public async Task EvaluateAsync_UnauthenticatedPrincipal_DeniesWithoutCallingPermissionEvaluator()
    {
        var permissionEvaluator = Substitute.For<IPermissionEvaluator>();
        var sut = new AuthorizationPolicyEvaluator(permissionEvaluator, []);

        var decision = await sut.EvaluateAsync(CreateAnonymousUser(), new AbacResource("pedidos"), "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be(AbacDecisionReasons.NotAuthenticated);
        await permissionEvaluator.DidNotReceive().EvaluateAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_MissingRbacPermission_DeniesWithoutEvaluatingAnyRule()
    {
        var permissionEvaluator = CreatePermissionEvaluator(EffectivePermissions.Empty);
        var rule = Substitute.For<IAbacRule>();
        rule.AppliesTo(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var sut = new AuthorizationPolicyEvaluator(permissionEvaluator, [rule]);
        var user = CreateAuthenticatedUser(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        var decision = await sut.EvaluateAsync(user, new AbacResource("pedidos"), "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be(AbacDecisionReasons.PermissionDenied("pedidos.aprobar"));
        await rule.DidNotReceive().EvaluateAsync(
            Arg.Any<AbacSubject>(), Arg.Any<AbacResource>(), Arg.Any<string>(), Arg.Any<AbacContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_RbacGrantedAndNoApplicableRules_Allows()
    {
        var permissionEvaluator = CreatePermissionEvaluator(PermissionsWith("pedidos.aprobar"));
        var sut = new AuthorizationPolicyEvaluator(permissionEvaluator, []);
        var user = CreateAuthenticatedUser(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        var decision = await sut.EvaluateAsync(user, new AbacResource("pedidos"), "aprobar");

        decision.Allowed.Should().BeTrue();
        decision.Reason.Should().Be(AbacDecisionReasons.Granted("pedidos.aprobar"));
    }

    [Fact]
    public async Task EvaluateAsync_RbacGrantedButRuleNotApplicable_IsSkippedAndAllows()
    {
        var permissionEvaluator = CreatePermissionEvaluator(PermissionsWith("pedidos.aprobar"));
        var rule = Substitute.For<IAbacRule>();
        rule.AppliesTo("pedidos", "aprobar").Returns(false);
        var sut = new AuthorizationPolicyEvaluator(permissionEvaluator, [rule]);
        var user = CreateAuthenticatedUser(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        var decision = await sut.EvaluateAsync(user, new AbacResource("pedidos"), "aprobar");

        decision.Allowed.Should().BeTrue();
        await rule.DidNotReceive().EvaluateAsync(
            Arg.Any<AbacSubject>(), Arg.Any<AbacResource>(), Arg.Any<string>(), Arg.Any<AbacContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_RbacGrantedButApplicableRuleDenies_DeniesWithRuleReason()
    {
        var permissionEvaluator = CreatePermissionEvaluator(PermissionsWith("pedidos.aprobar"));
        var rule = Substitute.For<IAbacRule>();
        rule.AppliesTo("pedidos", "aprobar").Returns(true);
        rule.EvaluateAsync(Arg.Any<AbacSubject>(), Arg.Any<AbacResource>(), "aprobar", Arg.Any<AbacContext>(), Arg.Any<CancellationToken>())
            .Returns(AbacRuleOutcome.Deny("monto-excede-limite"));
        var sut = new AuthorizationPolicyEvaluator(permissionEvaluator, [rule]);
        var user = CreateAuthenticatedUser(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        var decision = await sut.EvaluateAsync(user, new AbacResource("pedidos"), "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be(AbacDecisionReasons.RuleDenied("monto-excede-limite"));
    }

    [Fact]
    public async Task EvaluateAsync_MultipleRules_DenyOverridesAnyAllowOrNotApplicable()
    {
        var permissionEvaluator = CreatePermissionEvaluator(PermissionsWith("pedidos.aprobar"));
        var notApplicableRule = Substitute.For<IAbacRule>();
        notApplicableRule.AppliesTo("pedidos", "aprobar").Returns(true);
        notApplicableRule.EvaluateAsync(Arg.Any<AbacSubject>(), Arg.Any<AbacResource>(), "aprobar", Arg.Any<AbacContext>(), Arg.Any<CancellationToken>())
            .Returns(AbacRuleOutcome.NotApplicable("sin-restriccion"));
        var denyingRule = Substitute.For<IAbacRule>();
        denyingRule.AppliesTo("pedidos", "aprobar").Returns(true);
        denyingRule.EvaluateAsync(Arg.Any<AbacSubject>(), Arg.Any<AbacResource>(), "aprobar", Arg.Any<AbacContext>(), Arg.Any<CancellationToken>())
            .Returns(AbacRuleOutcome.Deny("fuera-de-sucursal"));
        var sut = new AuthorizationPolicyEvaluator(permissionEvaluator, [notApplicableRule, denyingRule]);
        var user = CreateAuthenticatedUser(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        var decision = await sut.EvaluateAsync(user, new AbacResource("pedidos"), "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be(AbacDecisionReasons.RuleDenied("fuera-de-sucursal"));
    }

    [Fact]
    public async Task EvaluateAsync_PassesConfiguredResourceAttributesAndContextToRule()
    {
        var permissionEvaluator = CreatePermissionEvaluator(PermissionsWith("pedidos.aprobar"));
        var rule = Substitute.For<IAbacRule>();
        rule.AppliesTo("pedidos", "aprobar").Returns(true);
        rule.EvaluateAsync(Arg.Any<AbacSubject>(), Arg.Any<AbacResource>(), "aprobar", Arg.Any<AbacContext>(), Arg.Any<CancellationToken>())
            .Returns(AbacRuleOutcome.NotApplicable("sin-restriccion"));
        var sut = new AuthorizationPolicyEvaluator(permissionEvaluator, [rule]);
        var user = CreateAuthenticatedUser(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["monto"] = 1500m });
        var context = new AbacContext(new Dictionary<string, object?> { ["ip"] = "10.0.0.1" });

        await sut.EvaluateAsync(user, resource, "aprobar", context);

        await rule.Received(1).EvaluateAsync(
            Arg.Is<AbacSubject>(s => s.Principal == user),
            Arg.Is<AbacResource>(r => r == resource),
            "aprobar",
            Arg.Is<AbacContext>(c => c == context),
            Arg.Any<CancellationToken>());
    }
}
