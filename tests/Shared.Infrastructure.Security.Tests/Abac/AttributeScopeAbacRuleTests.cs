using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Abac;

public class AttributeScopeAbacRuleTests
{
    private static AbacSubject CreateSubject(params Claim[] claims) =>
        new(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), EffectivePermissions.Empty);

    private static IOptions<AbacOptions> CreateOptions(params AbacScopeAttributeRule[] rules)
    {
        var options = new AbacOptions();
        foreach (var rule in rules)
        {
            options.ScopeRules.Add(rule);
        }

        return Options.Create(options);
    }

    [Fact]
    public void AppliesTo_ResourceTypeConfigured_ReturnsTrue()
    {
        var sut = new AttributeScopeAbacRule(CreateOptions(new AbacScopeAttributeRule
        {
            ResourceType = "pedidos",
            ResourceAttributeKey = "empresaId",
            ClaimType = "empresa_id",
        }));

        sut.AppliesTo("pedidos", "aprobar").Should().BeTrue();
        sut.AppliesTo("productos", "aprobar").Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_WildcardResourceType_ReturnsTrueForAnyResourceType()
    {
        var sut = new AttributeScopeAbacRule(CreateOptions(new AbacScopeAttributeRule
        {
            ResourceType = "*",
            ResourceAttributeKey = "empresaId",
            ClaimType = "empresa_id",
        }));

        sut.AppliesTo("pedidos", "aprobar").Should().BeTrue();
        sut.AppliesTo("productos", "crear").Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_ResourceEmpresaMatchesSubjectClaim_NotApplicable()
    {
        var sut = new AttributeScopeAbacRule(CreateOptions(new AbacScopeAttributeRule
        {
            ResourceType = "pedidos",
            ResourceAttributeKey = "empresaId",
            ClaimType = "empresa_id",
        }));
        var subject = CreateSubject(new Claim("empresa_id", "1"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["empresaId"] = "1" });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
    }

    [Fact]
    public async Task EvaluateAsync_ResourceEmpresaNotAmongSubjectClaims_Denies()
    {
        var sut = new AttributeScopeAbacRule(CreateOptions(new AbacScopeAttributeRule
        {
            ResourceType = "pedidos",
            ResourceAttributeKey = "empresaId",
            ClaimType = "empresa_id",
        }));
        var subject = CreateSubject(new Claim("empresa_id", "1"), new Claim("empresa_id", "2"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["empresaId"] = "3" });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Contain("empresaId=3");
    }

    [Fact]
    public async Task EvaluateAsync_SubjectHasNoScopeClaim_NotApplicable_NoRestriction()
    {
        // Un sujeto sin el claim de alcance configurado (por ejemplo, un rol de servicio sin
        // restricción de empresa) no queda bloqueado por esta regla -- política deliberada de "sin
        // dato, no se restringe" (ver AttributeScopeAbacRule).
        var sut = new AttributeScopeAbacRule(CreateOptions(new AbacScopeAttributeRule
        {
            ResourceType = "pedidos",
            ResourceAttributeKey = "empresaId",
            ClaimType = "empresa_id",
        }));
        var subject = CreateSubject();
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["empresaId"] = "3" });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
    }

    [Fact]
    public async Task EvaluateAsync_ResourceDoesNotDeclareAttribute_NotApplicable()
    {
        var sut = new AttributeScopeAbacRule(CreateOptions(new AbacScopeAttributeRule
        {
            ResourceType = "pedidos",
            ResourceAttributeKey = "empresaId",
            ClaimType = "empresa_id",
        }));
        var subject = CreateSubject(new Claim("empresa_id", "1"));
        var resource = new AbacResource("pedidos");

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
    }

    [Fact]
    public async Task EvaluateAsync_TwoScopeRulesEmpresaAndSucursal_BothEnforced()
    {
        var sut = new AttributeScopeAbacRule(CreateOptions(
            new AbacScopeAttributeRule { ResourceType = "pedidos", ResourceAttributeKey = "empresaId", ClaimType = "empresa_id" },
            new AbacScopeAttributeRule { ResourceType = "pedidos", ResourceAttributeKey = "sucursalId", ClaimType = "sucursal_id" }));
        var subject = CreateSubject(new Claim("empresa_id", "1"), new Claim("sucursal_id", "10"));
        var resourceOutOfSucursal = new AbacResource("pedidos", new Dictionary<string, object?>
        {
            ["empresaId"] = "1",
            ["sucursalId"] = "99",
        });

        var outcome = await sut.EvaluateAsync(subject, resourceOutOfSucursal, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Contain("sucursalId=99");
    }
}
