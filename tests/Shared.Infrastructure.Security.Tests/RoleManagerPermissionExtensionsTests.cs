using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

/// <summary>
/// F2-09: <see cref="RoleManagerPermissionExtensions.AddPermissionAsync{TRole}(RoleManager{TRole}, TRole, string)"/>/
/// <see cref="RoleManagerPermissionExtensions.RemovePermissionAsync{TRole}(RoleManager{TRole}, TRole, string)"/>
/// (nuevos en esta tarea) y los overloads que además invalidan el cache de permisos.
/// </summary>
public sealed class RoleManagerPermissionExtensionsTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public RoleManagerPermissionExtensionsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestIdentityDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<Domain.MultiTenancy.ITenantProvider>(_ => new FakeTenantProvider(null, isMultiTenancyEnabled: false));
        services.AddIdentityCore<TestApplicationUser>()
            .AddRoles<TestApplicationRole>()
            .AddEntityFrameworkStores<TestIdentityDbContext>();

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
    public async Task RemovePermissionAsync_RoleHadThePermission_RemovesItAndOtherPermissionsRemain()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();

        var role = new TestApplicationRole { Name = "Editor" };
        await roleManager.CreateAsync(role);
        await roleManager.AddPermissionAsync(role, "productos.crear");
        await roleManager.AddPermissionAsync(role, "productos.editar");

        var result = await roleManager.RemovePermissionAsync(role, "productos.crear");

        result.Succeeded.Should().BeTrue();
        var remaining = await roleManager.GetClaimsAsync(role);
        remaining.Should().ContainSingle(c => c.Type == PermissionClaimTypes.Permission && c.Value == "productos.editar");
    }

    [Fact]
    public async Task RemovePermissionAsync_RoleDidNotHaveThePermission_SucceedsWithoutError()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();

        var role = new TestApplicationRole { Name = "Editor" };
        await roleManager.CreateAsync(role);

        var result = await roleManager.RemovePermissionAsync(role, "productos.crear");

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AddPermissionAsync_WithInvalidator_SucceededOperation_InvalidatesRoleCache()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var invalidator = Substitute.For<IPermissionCacheInvalidator>();

        var role = new TestApplicationRole { Name = "Editor" };
        await roleManager.CreateAsync(role);

        var result = await roleManager.AddPermissionAsync(role, "productos.crear", invalidator);

        result.Succeeded.Should().BeTrue();
        await invalidator.Received(1).InvalidateRoleAsync("Editor", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemovePermissionAsync_WithInvalidator_SucceededOperation_InvalidatesRoleCache()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var invalidator = Substitute.For<IPermissionCacheInvalidator>();

        var role = new TestApplicationRole { Name = "Editor" };
        await roleManager.CreateAsync(role);
        await roleManager.AddPermissionAsync(role, "productos.crear");

        var result = await roleManager.RemovePermissionAsync(role, "productos.crear", invalidator);

        result.Succeeded.Should().BeTrue();
        await invalidator.Received(1).InvalidateRoleAsync("Editor", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddPermissionAsync_WithInvalidator_AlreadyGranted_StillInvalidatesRoleCache()
    {
        // IdentityResult.Success "de cortesía" (permiso ya concedido) sigue siendo un éxito -- invalidar
        // en ese caso es inocuo (la entrada cacheada, si existe, ya reflejaba este mismo permiso).
        await using var scope = await CreateInitializedScopeAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var invalidator = Substitute.For<IPermissionCacheInvalidator>();

        var role = new TestApplicationRole { Name = "Editor" };
        await roleManager.CreateAsync(role);
        await roleManager.AddPermissionAsync(role, "productos.crear");

        var result = await roleManager.AddPermissionAsync(role, "productos.crear", invalidator);

        result.Succeeded.Should().BeTrue();
        await invalidator.Received(1).InvalidateRoleAsync("Editor", Arg.Any<CancellationToken>());
    }

    private static IAuditWriter CreateAuditWriter()
    {
        var auditWriter = Substitute.For<IAuditWriter>();
        auditWriter.WriteAsync(Arg.Any<AuditEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<AuditEntryRequest>();
                var entry = new AuditEntry(
                    Guid.NewGuid(), DateTime.UtcNow, request.Actor, request.TenantId, request.Action,
                    request.Resource, request.Outcome, request.Reason, request.CorrelationId, request.TraceId,
                    request.IpAddress, request.Metadata, "hash");
                return Task.FromResult(Result.Success(entry));
            });
        return auditWriter;
    }

    [Fact]
    public async Task AddPermissionAsync_WithAuditWriter_SucceededOperation_WritesSuccessAuditEntryExactlyOnce()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var invalidator = Substitute.For<IPermissionCacheInvalidator>();
        var auditWriter = CreateAuditWriter();
        var actor = new AuditActor("admin-user-id", AuditActorType.User);

        var role = new TestApplicationRole { Name = "Editor" };
        await roleManager.CreateAsync(role);

        var result = await roleManager.AddPermissionAsync(role, "productos.crear", invalidator, auditWriter, actor);

        result.Succeeded.Should().BeTrue();
        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AuditEntryRequest>(r =>
                r.Actor.Id == "admin-user-id" &&
                r.Action == "roles.permission.grant" &&
                r.Resource.Type == "roles" &&
                r.Resource.Id == role.Id.ToString() &&
                r.Outcome == AuditOutcome.Success &&
                r.Metadata["permission"] == "productos.crear"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemovePermissionAsync_WithAuditWriter_SucceededOperation_WritesSuccessAuditEntryExactlyOnce()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var invalidator = Substitute.For<IPermissionCacheInvalidator>();
        var auditWriter = CreateAuditWriter();
        var actor = new AuditActor("admin-user-id", AuditActorType.User);

        var role = new TestApplicationRole { Name = "Editor" };
        await roleManager.CreateAsync(role);
        await roleManager.AddPermissionAsync(role, "productos.crear");

        var result = await roleManager.RemovePermissionAsync(role, "productos.crear", invalidator, auditWriter, actor);

        result.Succeeded.Should().BeTrue();
        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AuditEntryRequest>(r =>
                r.Action == "roles.permission.revoke" &&
                r.Outcome == AuditOutcome.Success &&
                r.Metadata["permission"] == "productos.crear"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddPermissionAsync_WithAuditWriter_FailedOperation_WritesErrorAuditEntryWithReason()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var invalidator = Substitute.For<IPermissionCacheInvalidator>();
        var auditWriter = CreateAuditWriter();
        var actor = new AuditActor("admin-user-id", AuditActorType.User);

        var role = new TestApplicationRole { Name = "Editor" };
        await roleManager.CreateAsync(role);

        // Simula un conflicto de concurrencia real (otro proceso modificó el rol entre la lectura y esta
        // escritura): Identity traduce DbUpdateConcurrencyException en un IdentityResult.Failed
        // (ConcurrencyFailure) -- el mismo tipo de fallo técnico esperado que debe quedar auditado como
        // AuditOutcome.Error, no como una denegación de autorización (AuditOutcome.Denied).
        await using var context = scope.ServiceProvider.GetRequiredService<TestIdentityDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE AspNetRoles SET ConcurrencyStamp = {0} WHERE Id = {1}",
            Guid.NewGuid().ToString(), role.Id);

        var result = await roleManager.AddPermissionAsync(role, "productos.crear", invalidator, auditWriter, actor);

        result.Succeeded.Should().BeFalse();
        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AuditEntryRequest>(r => r.Outcome == AuditOutcome.Error && r.Reason != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddPermissionAsync_WithAuditWriter_PropagatesTenantId()
    {
        await using var scope = await CreateInitializedScopeAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<TestApplicationRole>>();
        var invalidator = Substitute.For<IPermissionCacheInvalidator>();
        var auditWriter = CreateAuditWriter();
        var actor = new AuditActor("admin-user-id", AuditActorType.User);
        var tenantId = Guid.NewGuid();

        var role = new TestApplicationRole { Name = "Editor" };
        await roleManager.CreateAsync(role);

        await roleManager.AddPermissionAsync(role, "productos.crear", invalidator, auditWriter, actor, tenantId);

        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AuditEntryRequest>(r => r.TenantId == tenantId),
            Arg.Any<CancellationToken>());
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
