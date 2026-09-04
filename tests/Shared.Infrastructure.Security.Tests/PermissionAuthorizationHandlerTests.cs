using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

public class PermissionAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext CreateContext(
        PermissionRequirement requirement,
        ClaimsPrincipal user) =>
        new([requirement], user, null);

    [Fact]
    public async Task HandleRequirementAsync_UserHasPermission_Succeeds()
    {
        var userId = Guid.NewGuid();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(["productos.crear"]);
        var handler = new PermissionAuthorizationHandler(permissionService);
        var requirement = new PermissionRequirement("productos.crear");
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
        var context = CreateContext(requirement, user);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_UserLacksPermission_DoesNotSucceed()
    {
        var userId = Guid.NewGuid();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(["productos.editar"]);
        var handler = new PermissionAuthorizationHandler(permissionService);
        var requirement = new PermissionRequirement("productos.crear");
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
        var context = CreateContext(requirement, user);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_UserWithoutIdentifierClaim_DoesNotSucceed()
    {
        var permissionService = Substitute.For<IPermissionService>();
        var handler = new PermissionAuthorizationHandler(permissionService);
        var requirement = new PermissionRequirement("productos.crear");
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var context = CreateContext(requirement, anonymousUser);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        await permissionService.DidNotReceive().GetPermissionsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
