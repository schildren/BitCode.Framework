using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Abac;

public class AmountLimitAbacRuleTests
{
    private static AbacSubject CreateSubject(params Claim[] claims) =>
        new(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), EffectivePermissions.Empty);

    private static IOptions<AbacOptions> CreateOptions(params AbacAmountLimitRule[] rules)
    {
        var options = new AbacOptions();
        foreach (var rule in rules)
        {
            options.AmountLimitRules.Add(rule);
        }

        return Options.Create(options);
    }

    private static AbacAmountLimitRule DefaultRule() => new()
    {
        ResourceType = "pedidos",
        ResourceAttributeKey = "monto",
        ClaimType = "monto_maximo",
    };

    [Fact]
    public void AppliesTo_ResourceTypeConfigured_ReturnsTrue()
    {
        var sut = new AmountLimitAbacRule(CreateOptions(DefaultRule()));

        sut.AppliesTo("pedidos", "aprobar").Should().BeTrue();
        sut.AppliesTo("productos", "aprobar").Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_AmountWithinLimit_NotApplicable()
    {
        var sut = new AmountLimitAbacRule(CreateOptions(DefaultRule()));
        var subject = CreateSubject(new Claim("monto_maximo", "10000"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["monto"] = 5000m });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
    }

    [Fact]
    public async Task EvaluateAsync_AmountExceedsLimit_Denies()
    {
        var sut = new AmountLimitAbacRule(CreateOptions(DefaultRule()));
        var subject = CreateSubject(new Claim("monto_maximo", "10000"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["monto"] = 15000m });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Contain("monto=15000").And.Contain("monto_maximo=10000");
    }

    [Fact]
    public async Task EvaluateAsync_AmountEqualsLimit_NotApplicable()
    {
        var sut = new AmountLimitAbacRule(CreateOptions(DefaultRule()));
        var subject = CreateSubject(new Claim("monto_maximo", "10000"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["monto"] = 10000m });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
    }

    [Fact]
    public async Task EvaluateAsync_SubjectHasNoLimitClaim_NotApplicable_NoRestriction()
    {
        var sut = new AmountLimitAbacRule(CreateOptions(DefaultRule()));
        var subject = CreateSubject();
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["monto"] = 999999m });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
    }

    [Fact]
    public async Task EvaluateAsync_ResourceDoesNotDeclareAmount_NotApplicable()
    {
        var sut = new AmountLimitAbacRule(CreateOptions(DefaultRule()));
        var subject = CreateSubject(new Claim("monto_maximo", "10000"));
        var resource = new AbacResource("pedidos");

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
    }

    [Fact]
    public async Task EvaluateAsync_ClaimValueNotNumeric_NotApplicable()
    {
        var sut = new AmountLimitAbacRule(CreateOptions(DefaultRule()));
        var subject = CreateSubject(new Claim("monto_maximo", "no-es-un-numero"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["monto"] = 5000m });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
    }

    [Theory]
    [InlineData(5000)]
    [InlineData(5000L)]
    [InlineData(5000.0)]
    public async Task EvaluateAsync_AmountAsIntLongOrDouble_IsConvertedAndCompared(object rawAmount)
    {
        var sut = new AmountLimitAbacRule(CreateOptions(DefaultRule()));
        var subject = CreateSubject(new Claim("monto_maximo", "1000"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["monto"] = rawAmount });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
    }
}
