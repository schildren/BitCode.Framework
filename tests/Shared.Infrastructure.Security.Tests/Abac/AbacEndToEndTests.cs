using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Abac;

/// <summary>
/// Prueba de componente (F2-08): ejercita la pila real completa -- <see cref="IAuthorizationPolicyEvaluator"/>
/// resuelto vía contenedor de DI real, compuesto sobre el <see cref="PermissionEvaluator"/> real de F2-07 y
/// las dos <see cref="IAbacRule"/> incorporadas reales (<see cref="AttributeScopeAbacRule"/>,
/// <see cref="AmountLimitAbacRule"/>) -- sin mockear ninguna de esas piezas. Solo se sustituye
/// <see cref="IPermissionService"/> (la fuente de datos de Identity local, equivalente a
/// <c>UserManager</c>/<c>RoleManager</c> real, ya cubierto contra SQL Server real por
/// <c>PermissionServiceTests</c>/F1) para no depender de una base de datos en esta suite. Cubre el
/// criterio de aceptación literal de F2-08 ("reglas por monto, empresa y sucursal") de punta a punta:
/// un <see cref="ClaimsPrincipal"/> con permiso RBAC pero fuera de la empresa/sucursal/límite de monto
/// configurados es denegado por el evaluador combinado real, no por una regla mockeada.
/// </summary>
public class AbacEndToEndTests
{
    private static IServiceProvider BuildProvider(string[] userPermissions, Action<AbacOptions> configure)
    {
        var services = new ServiceCollection();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(userPermissions);
        services.AddSingleton(permissionService);
        services.AddSingleton(Substitute.For<ITenantContext>());

        services.AddSharedAbacAuthorization(configure);

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreateUser(Guid userId, params Claim[] extraClaims) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), .. extraClaims], "Test"));

    [Fact]
    public async Task EndToEnd_UserWithinEmpresaSucursalAndAmountLimit_Allows()
    {
        var provider = BuildProvider(["pedidos.aprobar"], options =>
        {
            options.ScopeRules.Add(new AbacScopeAttributeRule { ResourceType = "pedidos", ResourceAttributeKey = "empresaId", ClaimType = "empresa_id" });
            options.ScopeRules.Add(new AbacScopeAttributeRule { ResourceType = "pedidos", ResourceAttributeKey = "sucursalId", ClaimType = "sucursal_id" });
            options.AmountLimitRules.Add(new AbacAmountLimitRule { ResourceType = "pedidos", ResourceAttributeKey = "monto", ClaimType = "monto_maximo" });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid(), new Claim("empresa_id", "1"), new Claim("sucursal_id", "10"), new Claim("monto_maximo", "5000"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?>
        {
            ["empresaId"] = "1",
            ["sucursalId"] = "10",
            ["monto"] = 3000m,
        });

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task EndToEnd_UserOutsideConfiguredSucursal_Denies()
    {
        var provider = BuildProvider(["pedidos.aprobar"], options =>
        {
            options.ScopeRules.Add(new AbacScopeAttributeRule { ResourceType = "pedidos", ResourceAttributeKey = "empresaId", ClaimType = "empresa_id" });
            options.ScopeRules.Add(new AbacScopeAttributeRule { ResourceType = "pedidos", ResourceAttributeKey = "sucursalId", ClaimType = "sucursal_id" });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid(), new Claim("empresa_id", "1"), new Claim("sucursal_id", "10"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?>
        {
            ["empresaId"] = "1",
            ["sucursalId"] = "99",
        });

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().StartWith("abac:rule-denied:scope:sucursalId=99");
    }

    [Fact]
    public async Task EndToEnd_AmountExceedsUserLimit_Denies()
    {
        var provider = BuildProvider(["pedidos.aprobar"], options =>
        {
            options.AmountLimitRules.Add(new AbacAmountLimitRule { ResourceType = "pedidos", ResourceAttributeKey = "monto", ClaimType = "monto_maximo" });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid(), new Claim("monto_maximo", "1000"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["monto"] = 50000m });

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().StartWith("abac:rule-denied:amount-limit:monto=50000");
    }

    [Fact]
    public async Task EndToEnd_UserWithoutBaseRbacPermission_DeniesRegardlessOfAttributes()
    {
        // Default deny: sin el permiso RBAC base, ninguna regla ABAC llega a evaluarse -- ni siquiera
        // un recurso cuyos atributos coincidirían perfectamente con los del sujeto lo salva.
        var provider = BuildProvider([], options =>
        {
            options.ScopeRules.Add(new AbacScopeAttributeRule { ResourceType = "pedidos", ResourceAttributeKey = "empresaId", ClaimType = "empresa_id" });
        });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid(), new Claim("empresa_id", "1"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["empresaId"] = "1" });

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("rbac:permission-denied:pedidos.aprobar");
    }
}
