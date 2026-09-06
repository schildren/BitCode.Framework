using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.PrivilegedOperations;

public class SegregationOfDutiesAbacRuleTests
{
    private static AbacSubject CreateSubject(EffectivePermissions permissions, params Claim[] claims) =>
        new(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), permissions);

    private static AbacSubject CreateSubject(params Claim[] claims) =>
        CreateSubject(EffectivePermissions.Empty, claims);

    private static IOptions<PrivilegedOperationsOptions> CreateOptions(
        MakerCheckerRule[]? makerCheckerRules = null,
        MutuallyExclusivePermissionPair[]? exclusivePairs = null)
    {
        var options = new PrivilegedOperationsOptions();
        foreach (var rule in makerCheckerRules ?? [])
        {
            options.MakerCheckerRules.Add(rule);
        }

        foreach (var pair in exclusivePairs ?? [])
        {
            options.MutuallyExclusivePermissions.Add(pair);
        }

        return Options.Create(options);
    }

    [Fact]
    public void AppliesTo_MakerCheckerRuleConfiguredForResourceAndAction_ReturnsTrue()
    {
        var sut = new SegregationOfDutiesAbacRule(CreateOptions(makerCheckerRules:
        [
            new MakerCheckerRule { ResourceType = "pedidos", Action = "aprobar", ActorResourceAttributeKey = "creadoPorUserId" },
        ]));

        sut.AppliesTo("pedidos", "aprobar").Should().BeTrue();
        sut.AppliesTo("pedidos", "crear").Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_MutuallyExclusivePermissionsConfigured_ReturnsTrueRegardlessOfResource()
    {
        var sut = new SegregationOfDutiesAbacRule(CreateOptions(exclusivePairs:
        [
            new MutuallyExclusivePermissionPair { PermissionA = "pedidos.crear", PermissionB = "pedidos.auditar" },
        ]));

        sut.AppliesTo("cualquier-recurso", "cualquier-accion").Should().BeTrue();
    }

    [Fact]
    public void AppliesTo_NothingConfigured_ReturnsFalse()
    {
        var sut = new SegregationOfDutiesAbacRule(CreateOptions());

        sut.AppliesTo("pedidos", "aprobar").Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_DifferentCreatorAndApprover_Verified()
    {
        var sut = new SegregationOfDutiesAbacRule(CreateOptions(makerCheckerRules:
        [
            new MakerCheckerRule { ResourceType = "pedidos", Action = "aprobar", ActorResourceAttributeKey = "creadoPorUserId" },
        ]));
        var subject = CreateSubject(new Claim(ClaimTypes.NameIdentifier, "user-2"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["creadoPorUserId"] = "user-1" });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
        outcome.Reason.Should().Be("sod:verified");
    }

    [Fact]
    public async Task EvaluateAsync_SameActorCreatedAndApproves_Denies()
    {
        var sut = new SegregationOfDutiesAbacRule(CreateOptions(makerCheckerRules:
        [
            new MakerCheckerRule { ResourceType = "pedidos", Action = "aprobar", ActorResourceAttributeKey = "creadoPorUserId" },
        ]));
        var subject = CreateSubject(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["creadoPorUserId"] = "user-1" });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Be("sod:same-actor:creadoPorUserId=user-1");
    }

    [Fact]
    public async Task EvaluateAsync_SameActorDifferentCasing_Denies()
    {
        // El atributo de actor del recurso (típicamente un Guid puesto por el handler de negocio) y el
        // claim del sujeto (emitido por el IdP) pueden representar la MISMA identidad con distinto
        // casing -- la comparación debe ignorar mayúsculas/minúsculas, o la regla fallaría abierta
        // exactamente en el escenario que existe para prevenir (alguien aprobando su propia operación).
        var actorId = Guid.NewGuid();
        var sut = new SegregationOfDutiesAbacRule(CreateOptions(makerCheckerRules:
        [
            new MakerCheckerRule { ResourceType = "pedidos", Action = "aprobar", ActorResourceAttributeKey = "creadoPorUserId" },
        ]));
        var subject = CreateSubject(new Claim(ClaimTypes.NameIdentifier, actorId.ToString().ToLowerInvariant()));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?>
        {
            ["creadoPorUserId"] = actorId.ToString().ToUpperInvariant(),
        });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Be($"sod:same-actor:creadoPorUserId={actorId.ToString().ToUpperInvariant()}");
    }

    [Fact]
    public async Task EvaluateAsync_ResourceMissingActorAttribute_Denies_FailClosed()
    {
        // A diferencia de AttributeScopeAbacRule (F2-08, "sin dato, no se restringe"), una regla
        // maker-checker configurada y aplicable deniega si no puede probar que no hay conflicto.
        var sut = new SegregationOfDutiesAbacRule(CreateOptions(makerCheckerRules:
        [
            new MakerCheckerRule { ResourceType = "pedidos", Action = "aprobar", ActorResourceAttributeKey = "creadoPorUserId" },
        ]));
        var subject = CreateSubject(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        var resource = new AbacResource("pedidos");

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Be("sod:missing-actor-attribute:creadoPorUserId");
    }

    [Fact]
    public async Task EvaluateAsync_SubjectMissingClaim_Denies_FailClosed()
    {
        var sut = new SegregationOfDutiesAbacRule(CreateOptions(makerCheckerRules:
        [
            new MakerCheckerRule { ResourceType = "pedidos", Action = "aprobar", ActorResourceAttributeKey = "creadoPorUserId" },
        ]));
        var subject = CreateSubject();
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["creadoPorUserId"] = "user-1" });

        var outcome = await sut.EvaluateAsync(subject, resource, "aprobar", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Be($"sod:missing-subject-claim:{ClaimTypes.NameIdentifier}");
    }

    [Fact]
    public async Task EvaluateAsync_SubjectHoldsBothMutuallyExclusivePermissions_Denies()
    {
        var permissions = new EffectivePermissions(
        [
            new PermissionGrant("pedidos.crear", PermissionGrantSources.LocalIdentityRoles),
            new PermissionGrant("pedidos.auditar", PermissionGrantSources.LocalIdentityRoles),
        ]);
        var sut = new SegregationOfDutiesAbacRule(CreateOptions(exclusivePairs:
        [
            new MutuallyExclusivePermissionPair { PermissionA = "pedidos.crear", PermissionB = "pedidos.auditar" },
        ]));
        var subject = CreateSubject(permissions);

        var outcome = await sut.EvaluateAsync(subject, new AbacResource("pedidos"), "crear", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.Deny);
        outcome.Reason.Should().Be("sod:mutually-exclusive-permissions:pedidos.crear+pedidos.auditar");
    }

    [Fact]
    public async Task EvaluateAsync_SubjectHoldsOnlyOneOfExclusivePair_Verified()
    {
        var permissions = new EffectivePermissions(
        [
            new PermissionGrant("pedidos.crear", PermissionGrantSources.LocalIdentityRoles),
        ]);
        var sut = new SegregationOfDutiesAbacRule(CreateOptions(exclusivePairs:
        [
            new MutuallyExclusivePermissionPair { PermissionA = "pedidos.crear", PermissionB = "pedidos.auditar" },
        ]));
        var subject = CreateSubject(permissions);

        var outcome = await sut.EvaluateAsync(subject, new AbacResource("pedidos"), "crear", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
        outcome.Reason.Should().Be("sod:verified");
    }

    [Fact]
    public async Task EvaluateAsync_NothingConfiguredForThisEvaluation_NoRestriction()
    {
        var sut = new SegregationOfDutiesAbacRule(CreateOptions(makerCheckerRules:
        [
            new MakerCheckerRule { ResourceType = "pedidos", Action = "aprobar", ActorResourceAttributeKey = "creadoPorUserId" },
        ]));
        var subject = CreateSubject();

        var outcome = await sut.EvaluateAsync(subject, new AbacResource("productos"), "crear", AbacContext.Empty);

        outcome.Effect.Should().Be(AbacRuleEffect.NotApplicable);
        outcome.Reason.Should().Be("sod:no-restriction");
    }
}
