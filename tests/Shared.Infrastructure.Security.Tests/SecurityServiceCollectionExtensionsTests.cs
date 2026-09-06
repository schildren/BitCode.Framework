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
    public void AddSharedSecurity_WithoutJwtConfigurationSection_Throws()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestIdentityDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var emptyConfiguration = new ConfigurationBuilder().Build();

        var act = () => services.AddSharedSecurity<TestApplicationUser, TestApplicationRole, TestIdentityDbContext>(
            emptyConfiguration);

        act.Should().Throw<InvalidOperationException>();
    }
}
