using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

public class SecurityServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = "una-clave-secreta-de-al-menos-32-caracteres-para-hmacsha256",
                ["Jwt:Issuer"] = "BitCode.Framework.Tests",
                ["Jwt:Audience"] = "BitCode.Framework.Tests.Clients",
            })
            .Build();

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestIdentityDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<ITenantProvider>(_ => new FakeTenantProvider(null, isMultiTenancyEnabled: false));
        services.AddSharedSecurity<TestApplicationUser, TestApplicationRole, TestIdentityDbContext>(BuildConfiguration());

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSharedSecurity_ResolvesIdentityManagersJwtGeneratorAndPermissionService()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<UserManager<TestApplicationUser>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IPermissionService>().Should()
            .BeOfType<PermissionService<TestApplicationUser, TestApplicationRole>>();
    }

    [Fact]
    public void AddSharedSecurity_RegistersEffectivePermissionEvaluator()
    {
        // F2-07 (RBAC 2.0): PermissionAuthorizationHandler ya no depende directamente de
        // IPermissionService, sino del evaluador normalizado.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPermissionEvaluator>().Should().NotBeNull();
    }

    [Fact]
    public void AddSharedSecurity_RegistersDynamicPermissionPolicyProvider()
    {
        using var provider = BuildProvider();

        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        policyProvider.Should().BeOfType<PermissionAuthorizationPolicyProvider>();
    }

    [Fact]
    public void AddSharedSecurity_WithoutAddSharedPermissionCache_RegistersNullPermissionCacheInvalidator()
    {
        // F2-09: sin AddSharedPermissionCache, un proyecto puede seguir inyectando
        // IPermissionCacheInvalidator (no-op) sin condicionar su código a si el cache está habilitado.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPermissionCacheInvalidator>().Should()
            .BeOfType<NullPermissionCacheInvalidator>();
    }

    [Fact]
    public void AddSharedSecurity_WithoutJwtConfigurationSection_Throws()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestIdentityDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var emptyConfiguration = new ConfigurationBuilder().Build();

        var act = () => services.AddSharedSecurity<TestApplicationUser, TestApplicationRole, TestIdentityDbContext>(
            emptyConfiguration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedSecurity_CalledAfterAddSharedPermissionCache_Throws()
    {
        // Fix post-revisión de arquitectura de F2-09: antes de este fix, este orden de llamadas
        // (AddSharedPermissionEvaluation -> AddSharedPermissionCache -> AddSharedSecurity) no lanzaba
        // ninguna excepción -- el AddScoped<IPermissionService, PermissionService<TUser, TRole>> de
        // AddSharedSecurity agregaba un descriptor que ganaba la resolución sobre el Replace ya hecho por
        // AddSharedPermissionCache, dejando el decorador (CachedPermissionService) huérfano y sin efecto
        // de forma completamente silenciosa. Ahora AddSharedSecurity detecta ese estado y falla de forma
        // determinística en el arranque.
        var services = new ServiceCollection();
        services.AddDbContext<TestIdentityDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<ITenantProvider>(_ => new FakeTenantProvider(null, isMultiTenancyEnabled: false));
        services.AddSharedPermissionEvaluation();
        services.AddSharedPermissionCache();

        var act = () => services.AddSharedSecurity<TestApplicationUser, TestApplicationRole, TestIdentityDbContext>(
            BuildConfiguration());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddSharedSecurity*AddSharedPermissionCache*");
    }

    [Fact]
    public void AddSharedSecurity_CalledBeforeAddSharedPermissionCache_ResolvesCachedPermissionService()
    {
        // Orden correcto (documentado): AddSharedSecurity antes de AddSharedPermissionCache -- el
        // decorador debe aplicarse sobre PermissionService<TUser, TRole>, no quedar huérfano.
        var services = new ServiceCollection();
        services.AddDbContext<TestIdentityDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<ITenantProvider>(_ => new FakeTenantProvider(null, isMultiTenancyEnabled: false));
        services.AddSharedSecurity<TestApplicationUser, TestApplicationRole, TestIdentityDbContext>(BuildConfiguration());

        services.AddSharedPermissionCache();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPermissionService>().Should().BeOfType<CachedPermissionService>();
    }
}
