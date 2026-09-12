using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

public class PermissionServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public PermissionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestIdentityDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<Domain.MultiTenancy.ITenantProvider>(_ => new FakeTenantProvider(null, isMultiTenancyEnabled: false));
        services.AddIdentityCore<TestApplicationUser>()
            .AddRoles<TestApplicationRole>()
            .AddEntityFrameworkStores<TestIdentityDbContext>();
        services.AddScoped<IPermissionService, PermissionService<TestApplicationUser, TestApplicationRole>>();

        _provider = services.BuildServiceProvider();
    }

    private async Task<AsyncServiceScope> CreateInitializedScopeAsync()
    {
        var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestIdentityDbContext>();
        await context.Database.EnsureCreatedAsync();
        return scope;
    }

    [Fact]
    public async Task GetPermissionsForUserAsync_ReturnsPermissionsGrantedToUsersRoles()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TestApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var role = new TestApplicationRole { Name = "Editor" };
        await roleManager.CreateAsync(role);
        await roleManager.AddPermissionAsync(role, "productos.crear");
        await roleManager.AddPermissionAsync(role, "productos.editar");

        var user = new TestApplicationUser { UserName = "editor1", Email = "editor1@test.com" };
        await userManager.CreateAsync(user);
        await userManager.AddToRoleAsync(user, "Editor");

        var permissions = await permissionService.GetPermissionsForUserAsync(user.Id);

        permissions.Should().BeEquivalentTo(["productos.crear", "productos.editar"]);
    }

    [Fact]
    public async Task GetPermissionsForUserAsync_AggregatesPermissionsFromMultipleRolesWithoutDuplicates()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TestApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var roleA = new TestApplicationRole { Name = "RoleA" };
        var roleB = new TestApplicationRole { Name = "RoleB" };
        await roleManager.CreateAsync(roleA);
        await roleManager.CreateAsync(roleB);
        await roleManager.AddPermissionAsync(roleA, "productos.crear");
        await roleManager.AddPermissionAsync(roleB, "productos.crear");
        await roleManager.AddPermissionAsync(roleB, "productos.eliminar");

        var user = new TestApplicationUser { UserName = "multi", Email = "multi@test.com" };
        await userManager.CreateAsync(user);
        await userManager.AddToRolesAsync(user, ["RoleA", "RoleB"]);

        var permissions = await permissionService.GetPermissionsForUserAsync(user.Id);

        permissions.Should().BeEquivalentTo(["productos.crear", "productos.eliminar"]);
    }

    [Fact]
    public async Task GetPermissionsForUserAsync_WithUnknownUser_ReturnsEmpty()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var permissions = await permissionService.GetPermissionsForUserAsync(Guid.NewGuid());

        permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_ReturnsPermissionsGrantedToRole()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var role = new TestApplicationRole { Name = "Editor" };
        await roleManager.CreateAsync(role);
        await roleManager.AddPermissionAsync(role, "productos.crear");
        await roleManager.AddPermissionAsync(role, "productos.editar");

        var permissions = await permissionService.GetPermissionsForRoleAsync("Editor");

        permissions.Should().BeEquivalentTo(["productos.crear", "productos.editar"]);
    }

    [Fact]
    public async Task GetPermissionsForRoleAsync_WithUnknownRole_ReturnsEmpty()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var permissions = await permissionService.GetPermissionsForRoleAsync("Inexistente");

        permissions.Should().BeEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
