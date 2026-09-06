using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.PrivilegedOperations;

/// <summary>
/// Prueba de unidad de <see cref="AuditingAuthorizationPolicyEvaluator"/> (cierre del pendiente explícito
/// de F2-15): el evaluador interno y <see cref="IAuditWriter"/> se sustituyen ambos para poder verificar,
/// sin ambigüedad, que se invoca exactamente una vez por evaluación relevante y con los campos esperados.
/// </summary>
public class AuditingAuthorizationPolicyEvaluatorTests
{
    private static ClaimsPrincipal CreateUser(Guid userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));

    private static IOptions<PrivilegedOperationsOptions> CreateOptions(
        StepUpRequirement[]? stepUpRequirements = null,
        MakerCheckerRule[]? makerCheckerRules = null,
        MutuallyExclusivePermissionPair[]? mutuallyExclusivePermissions = null)
    {
        var options = new PrivilegedOperationsOptions();
        foreach (var requirement in stepUpRequirements ?? [])
        {
            options.StepUpRequirements.Add(requirement);
        }

        foreach (var rule in makerCheckerRules ?? [])
        {
            options.MakerCheckerRules.Add(rule);
        }

        foreach (var pair in mutuallyExclusivePermissions ?? [])
        {
            options.MutuallyExclusivePermissions.Add(pair);
        }

        return Options.Create(options);
    }

    private static (AuditingAuthorizationPolicyEvaluator Sut, IAuditWriter AuditWriter) CreateSut(
        AbacDecision innerDecision,
        IOptions<PrivilegedOperationsOptions> options,
        IAuditWriter? auditWriter = null,
        Guid? tenantId = null)
    {
        var inner = Substitute.For<IAuthorizationPolicyEvaluator>();
        inner.EvaluateAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AbacResource>(), Arg.Any<string>(), Arg.Any<AbacContext?>(), Arg.Any<CancellationToken>())
            .Returns(innerDecision);

        var effectiveAuditWriter = auditWriter ?? Substitute.For<IAuditWriter>();
        effectiveAuditWriter.WriteAsync(Arg.Any<AuditEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(Result.Success(BuildEntry(callInfo.Arg<AuditEntryRequest>()))));

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsMultiTenancyEnabled.Returns(tenantId is not null);
        tenantContext.TenantId.Returns(tenantId);

        var sut = new AuditingAuthorizationPolicyEvaluator(inner, effectiveAuditWriter, tenantContext, options);
        return (sut, effectiveAuditWriter);
    }

    private static AuditEntry BuildEntry(AuditEntryRequest request) =>
        new(Guid.NewGuid(), DateTime.UtcNow, request.Actor, request.TenantId, request.Action, request.Resource,
            request.Outcome, request.Reason, request.CorrelationId, request.TraceId, request.IpAddress,
            request.Metadata, "hash");

    [Fact]
    public async Task EvaluateAsync_PrivilegedOperationGranted_WritesSuccessAuditEntryExactlyOnce()
    {
        var options = CreateOptions(stepUpRequirements:
        [
            new StepUpRequirement { ResourceType = "pagos", Action = "aprobar", MaxAuthenticationAge = TimeSpan.FromMinutes(5) },
        ]);
        var (sut, auditWriter) = CreateSut(AbacDecision.Allow("abac:granted:pagos.aprobar"), options);
        var userId = Guid.NewGuid();
        var user = CreateUser(userId);
        var resource = new AbacResource("pagos");

        var decision = await sut.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeTrue();
        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AuditEntryRequest>(r =>
                r.Actor.Id == userId.ToString() &&
                r.Actor.Type == AuditActorType.User &&
                r.Action == "pagos.aprobar" &&
                r.Resource.Type == "pagos" &&
                r.Outcome == AuditOutcome.Success &&
                r.Reason == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_PrivilegedOperationDenied_WritesDeniedAuditEntryWithReasonExactlyOnce()
    {
        var options = CreateOptions(stepUpRequirements:
        [
            new StepUpRequirement { ResourceType = "pagos", Action = "aprobar", MaxAuthenticationAge = TimeSpan.FromMinutes(5) },
        ]);
        var deniedReason = "abac:rule-denied:step-up:missing-authentication-time:auth_time";
        var (sut, auditWriter) = CreateSut(AbacDecision.Deny(deniedReason), options);
        var userId = Guid.NewGuid();
        var user = CreateUser(userId);
        var resource = new AbacResource("pagos");

        var decision = await sut.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AuditEntryRequest>(r =>
                r.Outcome == AuditOutcome.Denied &&
                r.Reason == deniedReason &&
                r.Action == "pagos.aprobar"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_SegregationOfDutiesViolationDenied_WritesDeniedAuditEntry()
    {
        var options = CreateOptions(makerCheckerRules:
        [
            new MakerCheckerRule { ResourceType = "pedidos", Action = "aprobar", ActorResourceAttributeKey = "creadoPorUserId" },
        ]);
        var deniedReason = "abac:rule-denied:sod:same-actor:creadoPorUserId=abc";
        var (sut, auditWriter) = CreateSut(AbacDecision.Deny(deniedReason), options);
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("pedidos");

        await sut.EvaluateAsync(user, resource, "aprobar");

        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AuditEntryRequest>(r => r.Outcome == AuditOutcome.Denied && r.Reason == deniedReason),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_MutuallyExclusivePermissionsConfigured_TreatsAnyEvaluationAsPrivileged()
    {
        // MutuallyExclusivePermissions es una restricción de identidad (no depende del recurso/acción
        // concretos, ver SegregationOfDutiesAbacRule) -- basta con que haya al menos un par configurado
        // para que CUALQUIER evaluación se considere una operación privilegiada a efectos de auditoría.
        var options = CreateOptions(mutuallyExclusivePermissions:
        [
            new MutuallyExclusivePermissionPair { PermissionA = "pedidos.crear", PermissionB = "pedidos.auditar" },
        ]);
        var (sut, auditWriter) = CreateSut(AbacDecision.Allow("abac:granted:pedidos.crear"), options);
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("pedidos");

        await sut.EvaluateAsync(user, resource, "crear");

        await auditWriter.Received(1).WriteAsync(Arg.Any<AuditEntryRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_NonPrivilegedOperation_DoesNotWriteAuditEntry()
    {
        // Sin ninguna política de operaciones privilegiadas aplicable a este (resourceType, action), no se
        // audita -- evita ruido de auditoría sobre operaciones que el Plan Maestro no clasifica como
        // críticas (ver documentación de AuditingAuthorizationPolicyEvaluator).
        var options = CreateOptions(stepUpRequirements:
        [
            new StepUpRequirement { ResourceType = "pagos", Action = "aprobar", MaxAuthenticationAge = TimeSpan.FromMinutes(5) },
        ]);
        var (sut, auditWriter) = CreateSut(AbacDecision.Allow("abac:granted:pedidos.consultar"), options);
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("pedidos");

        await sut.EvaluateAsync(user, resource, "consultar");

        await auditWriter.DidNotReceive().WriteAsync(Arg.Any<AuditEntryRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_MultiTenancyEnabled_PropagatesResolvedTenantId()
    {
        var options = CreateOptions(stepUpRequirements:
        [
            new StepUpRequirement { ResourceType = "pagos", Action = "aprobar", MaxAuthenticationAge = TimeSpan.FromMinutes(5) },
        ]);
        var tenantId = Guid.NewGuid();
        var (sut, auditWriter) = CreateSut(AbacDecision.Allow("abac:granted:pagos.aprobar"), options, tenantId: tenantId);
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("pagos");

        await sut.EvaluateAsync(user, resource, "aprobar");

        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AuditEntryRequest>(r => r.TenantId == tenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_AuditWriteFails_StillReturnsInnerDecisionUnaltered()
    {
        // Un fallo transitorio de IAuditWriter nunca debe bloquear ni alterar la decisión de autorización
        // ya tomada -- ver la documentación de la clase.
        var options = CreateOptions(stepUpRequirements:
        [
            new StepUpRequirement { ResourceType = "pagos", Action = "aprobar", MaxAuthenticationAge = TimeSpan.FromMinutes(5) },
        ]);
        var auditWriter = Substitute.For<IAuditWriter>();
        auditWriter.WriteAsync(Arg.Any<AuditEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<AuditEntry>(Error.Failure("audit.unavailable", "no disponible"))));
        var (sut, _) = CreateSut(AbacDecision.Allow("abac:granted:pagos.aprobar"), options, auditWriter);
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("pagos");

        var decision = await sut.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeTrue();
    }
}
