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
    public async Task HandleRequirementAsync_EvaluatorGrantsPermission_Succeeds()
    {
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"));
        evaluator.EvaluateAsync(user, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissions([new PermissionGrant("productos.crear", PermissionGrantSources.LocalIdentityRoles)]));
        var handler = new PermissionAuthorizationHandler(evaluator);
        var requirement = new PermissionRequirement("productos.crear");
        var context = CreateContext(requirement, user);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_EvaluatorDoesNotGrantPermission_DoesNotSucceed()
    {
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"));
        evaluator.EvaluateAsync(user, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissions([new PermissionGrant("productos.editar", PermissionGrantSources.LocalIdentityRoles)]));
        var handler = new PermissionAuthorizationHandler(evaluator);
        var requirement = new PermissionRequirement("productos.crear");
        var context = CreateContext(requirement, user);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_EvaluatorReturnsEmpty_DoesNotSucceed()
    {
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        evaluator.EvaluateAsync(anonymousUser, Arg.Any<CancellationToken>())
            .Returns(EffectivePermissions.Empty);
        var handler = new PermissionAuthorizationHandler(evaluator);
        var requirement = new PermissionRequirement("productos.crear");
        var context = CreateContext(requirement, anonymousUser);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
