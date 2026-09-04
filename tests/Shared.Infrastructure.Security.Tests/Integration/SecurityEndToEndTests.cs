using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using BitCode.Framework.Shared.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Integration;

/// <summary>
/// Cierra la Fase 3 verificando contra un SQL Server real (Testcontainers) el flujo completo:
/// crear usuario y rol con Identity real, otorgar un permiso vía claim de rol, emitir un JWT,
/// resolver los permisos del usuario desde la base real, y confirmar que
/// PermissionAuthorizationHandler aprueba/deniega correctamente — todo cableado a través de
/// AddSharedSecurity tal como lo usaría un consumidor real.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class SecurityEndToEndTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("BitCodeFrameworkSecurity", testName);

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = "una-clave-secreta-de-al-menos-32-caracteres-para-hmacsha256",
                ["Jwt:Issuer"] = "BitCode.Framework.IntegrationTests",
                ["Jwt:Audience"] = "BitCode.Framework.IntegrationTests.Clients",
            })
            .Build();

    [Fact]
    public async Task FullFlow_UserWithPermission_GetsJwtAndPassesAuthorizationHandler()
    {
        var connectionString = BuildIsolatedConnectionString();
        var services = new ServiceCollection();
        services.AddDbContext<TestIdentityDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ITenantProvider>(_ => new FakeTenantProvider(null, isMultiTenancyEnabled: false));
        services.AddSharedSecurity<TestApplicationUser, TestApplicationRole, TestIdentityDbContext>(BuildConfiguration());

        await using var provider = services.BuildServiceProvider();
        await using (var setupScope = provider.CreateAsyncScope())
        {
            await setupScope.ServiceProvider.GetRequiredService<TestIdentityDbContext>().Database.EnsureCreatedAsync();
        }

        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TestApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var authorizationHandlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>();

        var role = new TestApplicationRole { Name = "Ventas" };
        await roleManager.CreateAsync(role);
        await roleManager.AddPermissionAsync(role, "productos.crear");

        var user = new TestApplicationUser { UserName = "vendedor1", Email = "vendedor1@test.com" };
        await userManager.CreateAsync(user);
        await userManager.AddToRoleAsync(user, "Ventas");

        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenGenerator.GenerateAccessToken(user, roles, []);
        accessToken.Should().NotBeNullOrWhiteSpace();

        var permissions = await permissionService.GetPermissionsForUserAsync(user.Id);
        permissions.Should().Contain("productos.crear");

        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "Test"));
        var requirement = new PermissionRequirement("productos.crear");
        var authContext = new AuthorizationHandlerContext([requirement], claimsPrincipal, null);

        foreach (var handler in authorizationHandlers)
        {
            await handler.HandleAsync(authContext);
        }

        authContext.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task FullFlow_UserWithoutPermission_FailsAuthorizationHandler()
    {
        var connectionString = BuildIsolatedConnectionString();
        var services = new ServiceCollection();
        services.AddDbContext<TestIdentityDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ITenantProvider>(_ => new FakeTenantProvider(null, isMultiTenancyEnabled: false));
        services.AddSharedSecurity<TestApplicationUser, TestApplicationRole, TestIdentityDbContext>(BuildConfiguration());

        await using var provider = services.BuildServiceProvider();
        await using (var setupScope = provider.CreateAsyncScope())
        {
            await setupScope.ServiceProvider.GetRequiredService<TestIdentityDbContext>().Database.EnsureCreatedAsync();
        }

        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TestApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var authorizationHandlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>();

        var role = new TestApplicationRole { Name = "Lectura" };
        await roleManager.CreateAsync(role);
        await roleManager.AddPermissionAsync(role, "productos.ver");

        var user = new TestApplicationUser { UserName = "lector1", Email = "lector1@test.com" };
        await userManager.CreateAsync(user);
        await userManager.AddToRoleAsync(user, "Lectura");

        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "Test"));
        var requirement = new PermissionRequirement("productos.crear");
        var authContext = new AuthorizationHandlerContext([requirement], claimsPrincipal, null);

        foreach (var handler in authorizationHandlers)
        {
            await handler.HandleAsync(authContext);
        }

        authContext.HasSucceeded.Should().BeFalse();
    }
}
