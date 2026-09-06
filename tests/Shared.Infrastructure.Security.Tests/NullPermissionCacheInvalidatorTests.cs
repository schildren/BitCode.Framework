using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

public class NullPermissionCacheInvalidatorTests
{
    [Fact]
    public async Task InvalidateUserAsync_IsNoOpAndCompletesSuccessfully()
    {
        var sut = new NullPermissionCacheInvalidator();

        var act = async () => await sut.InvalidateUserAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvalidateRoleAsync_IsNoOpAndCompletesSuccessfully()
    {
        var sut = new NullPermissionCacheInvalidator();

        var act = async () => await sut.InvalidateRoleAsync("Editor");

        await act.Should().NotThrowAsync();
    }
}
