using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.PrivilegedOperations;

/// <summary>
/// Prueba de componente (F2-10): ejercita la pila real completa -- <see cref="IAuthorizationPolicyEvaluator"/>
/// resuelto vía contenedor de DI real, compuesto sobre el <see cref="PermissionEvaluator"/> real de F2-07
/// y las reglas ABAC de F2-08/F2-10 reales, sin mockear ninguna de esas piezas. Solo se sustituye
/// <see cref="IPermissionService"/> (mismo criterio que <c>AbacEndToEndTests</c>). Cubre el criterio de
/// aceptación literal de F2-10 ("operaciones críticas protegidas") de punta a punta: un
/// <see cref="ClaimsPrincipal"/> con permiso RBAC pero sin evidencia de step-up, o en conflicto de
/// segregación de funciones, es denegado por el evaluador combinado real.
/// </summary>
public class PrivilegedOperationsEndToEndTests
{
    private static IServiceProvider BuildProvider(string[] userPermissions, Action<PrivilegedOperationsOptions> configure)
    {
        var services = new ServiceCollection();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(userPermissions);
        services.AddSingleton(permissionService);
        services.AddSingleton(Substitute.For<ITenantContext>());

        services.AddSharedAbacAuthorization();
        services.AddSharedPrivilegedOperationsPolicies(configure);

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreateUser(Guid userId, params Claim[] extraClaims) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), .. extraClaims], "Test"));

    [Fact]
    public async Task EndToEnd_CriticalOperationWithRecentStepUp_Allows()
    {
        var provider = BuildProvider(["pagos.aprobar"], options =>
        {
            options.StepUpRequirements.Add(new StepUpRequirement
            {
                ResourceType = "pagos",
                Action = "aprobar",
                MaxAuthenticationAge = TimeSpan.FromMinutes(5),
            });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString();
        var user = CreateUser(Guid.NewGuid(), new Claim("auth_time", authTime));
        var resource = new AbacResource("pagos");

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task EndToEnd_CriticalOperationWithoutStepUpEvidence_DeniesEvenWithRbacPermission()
    {
        var provider = BuildProvider(["pagos.aprobar"], options =>
        {
            options.StepUpRequirements.Add(new StepUpRequirement
            {
                ResourceType = "pagos",
                Action = "aprobar",
                MaxAuthenticationAge = TimeSpan.FromMinutes(5),
            });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("pagos");

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("abac:rule-denied:step-up:missing-authentication-time:auth_time");
    }

    [Fact]
    public async Task EndToEnd_CriticalOperationWithStaleStepUp_Denies()
    {
        var provider = BuildProvider(["pagos.aprobar"], options =>
        {
            options.StepUpRequirements.Add(new StepUpRequirement
            {
                ResourceType = "pagos",
                Action = "aprobar",
                MaxAuthenticationAge = TimeSpan.FromMinutes(5),
            });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var authTime = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds().ToString();
        var user = CreateUser(Guid.NewGuid(), new Claim("auth_time", authTime));
        var resource = new AbacResource("pagos");

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("abac:rule-denied:step-up:authentication-too-old:auth_time");
    }

    [Fact]
    public async Task EndToEnd_MakerCheckerSameActor_Denies()
    {
        var provider = BuildProvider(["pedidos.aprobar"], options =>
        {
            options.MakerCheckerRules.Add(new MakerCheckerRule
            {
                ResourceType = "pedidos",
                Action = "aprobar",
                ActorResourceAttributeKey = "creadoPorUserId",
            });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var userId = Guid.NewGuid();
        var user = CreateUser(userId);
        var resource = new AbacResource("pedidos", new Dictionary<string, object?>
        {
            ["creadoPorUserId"] = userId.ToString(),
        });

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be($"abac:rule-denied:sod:same-actor:creadoPorUserId={userId}");
    }

    [Fact]
    public async Task EndToEnd_MakerCheckerDifferentActor_Allows()
    {
        var provider = BuildProvider(["pedidos.aprobar"], options =>
        {
            options.MakerCheckerRules.Add(new MakerCheckerRule
            {
                ResourceType = "pedidos",
                Action = "aprobar",
                ActorResourceAttributeKey = "creadoPorUserId",
            });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("pedidos", new Dictionary<string, object?>
        {
            ["creadoPorUserId"] = Guid.NewGuid().ToString(),
        });

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task EndToEnd_MutuallyExclusivePermissions_Denies()
    {
        var provider = BuildProvider(["pedidos.crear", "pedidos.auditar"], options =>
        {
            options.MutuallyExclusivePermissions.Add(new MutuallyExclusivePermissionPair
            {
                PermissionA = "pedidos.crear",
                PermissionB = "pedidos.auditar",
            });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("pedidos");

        var decision = await evaluator.EvaluateAsync(user, resource, "crear");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("abac:rule-denied:sod:mutually-exclusive-permissions:pedidos.crear+pedidos.auditar");
    }

    [Fact]
    public async Task EndToEnd_UserWithoutBaseRbacPermission_DeniesRegardlessOfStepUpEvidence()
    {
        // Default deny: sin el permiso RBAC base, ninguna regla ABAC/privilegiada llega a evaluarse --
        // ni siquiera un step-up perfectamente satisfecho salva la falta del permiso base.
        var provider = BuildProvider([], options =>
        {
            options.StepUpRequirements.Add(new StepUpRequirement
            {
                ResourceType = "pagos",
                Action = "aprobar",
                MaxAuthenticationAge = TimeSpan.FromMinutes(5),
            });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString();
        var user = CreateUser(Guid.NewGuid(), new Claim("auth_time", authTime));
        var resource = new AbacResource("pagos");

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("rbac:permission-denied:pagos.aprobar");
    }

    [Fact]
    public async Task EndToEnd_CriticalOperationWithRecentStepUp_WritesSuccessAuditEntryExactlyOnce()
    {
        // Cierre del pendiente explícito de F2-15 (docs/guia-auditoria-inmutable.md): la pila real
        // compuesta por AddSharedPrivilegedOperationsPolicies debe emitir auditoría, sin mockear
        // IAuthorizationPolicyEvaluator ni IAuditWriter -- solo IPermissionService, mismo criterio que el
        // resto de esta clase.
        var provider = BuildProvider(["pagos.aprobar"], options =>
        {
            options.StepUpRequirements.Add(new StepUpRequirement
            {
                ResourceType = "pagos",
                Action = "aprobar",
                MaxAuthenticationAge = TimeSpan.FromMinutes(5),
            });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var auditWriter = (InMemoryAuditWriter)provider.GetRequiredService<IAuditWriter>();
        var userId = Guid.NewGuid();
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString();
        var user = CreateUser(userId, new Claim("auth_time", authTime));
        var resource = new AbacResource("pagos");

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeTrue();
        var entries = auditWriter.Entries;
        entries.Should().ContainSingle();
        entries[0].Actor.Id.Should().Be(userId.ToString());
        entries[0].Action.Should().Be("pagos.aprobar");
        entries[0].Outcome.Should().Be(AuditOutcome.Success);
    }

    [Fact]
    public async Task EndToEnd_CriticalOperationWithoutStepUpEvidence_WritesDeniedAuditEntryExactlyOnce()
    {
        var provider = BuildProvider(["pagos.aprobar"], options =>
        {
            options.StepUpRequirements.Add(new StepUpRequirement
            {
                ResourceType = "pagos",
                Action = "aprobar",
                MaxAuthenticationAge = TimeSpan.FromMinutes(5),
            });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var auditWriter = (InMemoryAuditWriter)provider.GetRequiredService<IAuditWriter>();
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("pagos");

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        var entries = auditWriter.Entries;
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(AuditOutcome.Denied);
        entries[0].Reason.Should().Be(decision.Reason);
    }

    [Fact]
    public async Task EndToEnd_NonPrivilegedOperation_DoesNotWriteAnyAuditEntry()
    {
        var provider = BuildProvider(["pedidos.consultar"], options =>
        {
            options.StepUpRequirements.Add(new StepUpRequirement
            {
                ResourceType = "pagos",
                Action = "aprobar",
                MaxAuthenticationAge = TimeSpan.FromMinutes(5),
            });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var auditWriter = (InMemoryAuditWriter)provider.GetRequiredService<IAuditWriter>();
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("pedidos");

        await evaluator.EvaluateAsync(user, resource, "consultar");

        auditWriter.Entries.Should().BeEmpty();
    }
}
