using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

public class PermissionAuthorizationPolicyProviderTests
{
    private static PermissionAuthorizationPolicyProvider CreateProvider(AuthorizationOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? new AuthorizationOptions()));

    [Fact]
    public async Task GetPolicyAsync_ForUnknownPolicyName_SynthesizesPermissionRequirement()
    {
        var provider = CreateProvider();

        var policy = await provider.GetPolicyAsync("productos.crear");

        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle(r => r is PermissionRequirement);
        ((PermissionRequirement)policy.Requirements.Single()).Permission.Should().Be("productos.crear");
    }

    [Fact]
    public async Task GetPolicyAsync_ForExplicitlyRegisteredPolicy_ReturnsThatPolicyInstead()
    {
        var explicitPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        var options = new AuthorizationOptions();
        options.AddPolicy("AdminOnly", explicitPolicy);
        var provider = CreateProvider(options);

        var policy = await provider.GetPolicyAsync("AdminOnly");

        policy.Should().BeSameAs(explicitPolicy);
        policy!.Requirements.Should().NotContain(r => r is PermissionRequirement);
    }
}
