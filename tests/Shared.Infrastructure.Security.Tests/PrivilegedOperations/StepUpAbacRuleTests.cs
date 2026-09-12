using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.PrivilegedOperations;

public class StepUpAbacRuleTests
{
    private static AbacSubject CreateSubject(params Claim[] claims) =>
        new(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), EffectivePermissions.Empty);

    private static IOptions<PrivilegedOperationsOptions> CreateOptions(params StepUpRequirement[] requirements)
    {
        var options = new PrivilegedOperationsOptions();
        foreach (var requirement in requirements)
        {
            options.StepUpRequirements.Add(requirement);
        }

        return Options.Create(options);
    }

    private static Claim AuthTimeClaim(TimeSpan age) =>
        new("auth_time", DateTimeOffset.UtcNow.Subtract(age).ToUnixTimeSeconds().ToString());

    [Fact]
    public void AppliesTo_ResourceAndActionConfigured_ReturnsTrue()
    {
        var sut = new StepUpAbacRule(CreateOptions(new StepUpRequirement
        {
            ResourceType = "pagos",
            Action = "aprobar",
            MaxAuthenticationAge = TimeSpan.FromMinutes(5),
        }));

        sut.AppliesTo("pagos", "aprobar").Should().BeTrue();
        sut.AppliesTo("pagos", "consultar").Should().BeFalse();
        sut.AppliesTo("pedidos", "aprobar").Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_WildcardAction_ReturnsTrueForAnyAction()
    {
        var sut = new StepUpAbacRule(CreateOptions(new StepUpRequirement
        {
            ResourceType = "pagos",
            MaxAuthenticationAge = TimeSpan.FromMinutes(5),
        }));

        sut.AppliesTo("pagos", "aprobar").Should().BeTrue();
        sut.AppliesTo("pagos", "eliminar").Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_RecentAuthTimeWithinMaxAge_Verified()
    {
        var sut = new StepUpAbacRule(CreateOptions(new StepUpRequirement
        {
            ResourceType = "pagos",
            Action = "aprobar",
            MaxAuthenticationAge = TimeSpan.FromMinutes(5),
        }));
        var subject = CreateSubject(AuthTimeClaim(TimeSpan.FromMinutes(1)));

        var outcome = await sut.EvaluateAsync(subject, new AbacResource("pagos"), "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
        outcome.Reason.Should().Be("step-up:verified");
    }

    [Fact]
    public async Task EvaluateAsync_AuthTimeOlderThanMaxAge_Denies()
    {
        var sut = new StepUpAbacRule(CreateOptions(new StepUpRequirement
        {
            ResourceType = "pagos",
            Action = "aprobar",
            MaxAuthenticationAge = TimeSpan.FromMinutes(5),
        }));
        var subject = CreateSubject(AuthTimeClaim(TimeSpan.FromMinutes(30)));

        var outcome = await sut.EvaluateAsync(subject, new AbacResource("pagos"), "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Be("step-up:authentication-too-old:auth_time");
    }

    [Fact]
    public async Task EvaluateAsync_MissingAuthTimeClaim_Denies_FailClosed()
    {
        // A diferencia de las reglas ABAC genéricas de F2-08 ("sin dato, no se restringe"), un requisito
        // de step-up sin evidencia deniega -- la ausencia del claim no puede interpretarse como "no
        // aplica ninguna restricción" para una operación explícitamente marcada como crítica.
        var sut = new StepUpAbacRule(CreateOptions(new StepUpRequirement
        {
            ResourceType = "pagos",
            Action = "aprobar",
            MaxAuthenticationAge = TimeSpan.FromMinutes(5),
        }));
        var subject = CreateSubject();

        var outcome = await sut.EvaluateAsync(subject, new AbacResource("pagos"), "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Be("step-up:missing-authentication-time:auth_time");
    }

    [Fact]
    public async Task EvaluateAsync_AcceptableAuthenticationMethodPresent_Verified()
    {
        var sut = new StepUpAbacRule(CreateOptions(new StepUpRequirement
        {
            ResourceType = "pagos",
            Action = "aprobar",
            AcceptableAuthenticationMethods = ["mfa"],
        }));
        var subject = CreateSubject(new Claim("amr", "mfa"));

        var outcome = await sut.EvaluateAsync(subject, new AbacResource("pagos"), "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
    }

    [Fact]
    public async Task EvaluateAsync_AuthenticationMethodNotAmongAcceptable_Denies()
    {
        var sut = new StepUpAbacRule(CreateOptions(new StepUpRequirement
        {
            ResourceType = "pagos",
            Action = "aprobar",
            AcceptableAuthenticationMethods = ["mfa", "hwk"],
        }));
        var subject = CreateSubject(new Claim("amr", "pwd"));

        var outcome = await sut.EvaluateAsync(subject, new AbacResource("pagos"), "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Be("step-up:missing-authentication-method:amr");
    }

    [Fact]
    public async Task EvaluateAsync_BothCriteriaConfigured_BothMustPass()
    {
        var sut = new StepUpAbacRule(CreateOptions(new StepUpRequirement
        {
            ResourceType = "pagos",
            Action = "aprobar",
            AcceptableAuthenticationMethods = ["mfa"],
            MaxAuthenticationAge = TimeSpan.FromMinutes(5),
        }));
        var subject = CreateSubject(new Claim("amr", "mfa"), AuthTimeClaim(TimeSpan.FromMinutes(30)));

        var outcome = await sut.EvaluateAsync(subject, new AbacResource("pagos"), "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Be("step-up:authentication-too-old:auth_time");
    }

    [Fact]
    public async Task EvaluateAsync_NoMatchingRequirement_NotRequired()
    {
        var sut = new StepUpAbacRule(CreateOptions(new StepUpRequirement
        {
            ResourceType = "pagos",
            Action = "aprobar",
            MaxAuthenticationAge = TimeSpan.FromMinutes(5),
        }));
        var subject = CreateSubject();

        var outcome = await sut.EvaluateAsync(subject, new AbacResource("productos"), "crear", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
        outcome.Reason.Should().Be("step-up:not-required");
    }

    [Fact]
    public async Task EvaluateAsync_RequirementWithoutAnyCriterion_ThrowsInvalidOperationException()
    {
        var sut = new StepUpAbacRule(CreateOptions(new StepUpRequirement
        {
            ResourceType = "pagos",
            Action = "aprobar",
        }));
        var subject = CreateSubject();

        var act = () => sut.EvaluateAsync(subject, new AbacResource("pagos"), "aprobar", AbacContext.Empty);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
