using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Abac;

public class AbacServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharedAbacAuthorization_RegistersEvaluatorAndBuiltInRules()
    {
        var services = new ServiceCollection();

        services.AddSharedAbacAuthorization();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAuthorizationPolicyEvaluator>().Should().BeOfType<AuthorizationPolicyEvaluator>();
        var rules = provider.GetServices<IAbacRule>().ToArray();
        rules.Should().Contain(r => r is AttributeScopeAbacRule);
        rules.Should().Contain(r => r is AmountLimitAbacRule);
    }

    [Fact]
    public void AddSharedAbacAuthorization_AppliesConfigureOptionsDelegate()
    {
        var services = new ServiceCollection();

        services.AddSharedAbacAuthorization(options =>
        {
            options.ScopeRules.Add(new AbacScopeAttributeRule
            {
                ResourceType = "pedidos",
                ResourceAttributeKey = "empresaId",
                ClaimType = "empresa_id",
            });
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AbacOptions>>().Value;
        options.ScopeRules.Should().ContainSingle(r => r.ResourceType == "pedidos");
    }

    [Fact]
    public void AddSharedAbacAuthorization_CalledAloneWithoutRbacRegistration_StillResolvesPermissionEvaluator()
    {
        // ABAC compone RBAC (F2-07) como la pieza base del criterio combinado -- debe funcionar aunque
        // el proyecto consumidor no haya llamado explícitamente a AddSharedSecurity/
        // AddSharedOidcAuthentication ni a AddSharedPermissionEvaluation por su cuenta.
        var services = new ServiceCollection();

        services.AddSharedAbacAuthorization();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPermissionEvaluator>().Should().NotBeNull();
    }

    [Fact]
    public void AddSharedAbacAuthorization_CalledAfterAddSharedPermissionEvaluation_DoesNotDuplicateAuthorizationHandler()
    {
        // Regresión del bugfix de F2-07 hecho en esta tarea (F2-08): antes, dos llamadas a
        // AddSharedPermissionEvaluation (una desde AddSharedSecurity/AddSharedOidcAuthentication, otra
        // desde AddSharedAbacAuthorization) duplicaban el registro de IAuthorizationHandler.
        var services = new ServiceCollection();

        services.AddSharedPermissionEvaluation();
        services.AddSharedAbacAuthorization();

        using var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IAuthorizationHandler>()
            .Where(h => h is PermissionAuthorizationHandler)
            .ToArray();
        handlers.Should().ContainSingle();
    }
}
