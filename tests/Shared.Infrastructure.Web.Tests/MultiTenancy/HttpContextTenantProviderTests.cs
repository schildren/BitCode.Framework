using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Web.MultiTenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace BitCode.Framework.Shared.Infrastructure.Web.Tests.MultiTenancy;

/// <summary>
/// F1-12: valida que <see cref="HttpContextTenantProvider"/> resuelve el TenantId únicamente desde
/// el claim <see cref="TenantClaimTypes.TenantId"/> de <see cref="HttpContext.User"/> — nunca desde
/// un header o query string — y que falla de forma segura (nunca con un tenant por defecto) cuando
/// ese claim no está presente en un request autenticado.
/// </summary>
public class HttpContextTenantProviderTests
{
    private static HttpContextTenantProvider CreateProvider(HttpContext? httpContext)
    {
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new HttpContextTenantProvider(accessor);
    }

    [Fact]
    public void TenantId_ResolvesFromTenantClaim_WhenUserIsAuthenticated()
    {
        var tenantId = Guid.NewGuid();
        var identity = new ClaimsIdentity(
            [new Claim(TenantClaimTypes.TenantId, tenantId.ToString())],
            authenticationType: "Test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var provider = CreateProvider(httpContext);

        provider.IsMultiTenancyEnabled.Should().BeTrue();
        provider.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void TenantId_IsNull_WhenNoHttpContext()
    {
        var provider = CreateProvider(httpContext: null);

        provider.TenantId.Should().BeNull("sin HttpContext (p. ej. un job) el provider falla cerrado, nunca asume un tenant");
    }

    [Fact]
    public void TenantId_IsNull_WhenUserIsNotAuthenticated()
    {
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

        var provider = CreateProvider(httpContext);

        provider.TenantId.Should().BeNull();
    }

    [Fact]
    public void TenantId_Throws_WhenAuthenticatedUserHasNoTenantClaim()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            authenticationType: "Test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var provider = CreateProvider(httpContext);

        var act = () => provider.TenantId;

        act.Should().Throw<TenantResolutionException>(
            "un usuario autenticado sin claim de tenant nunca debe resolverse a un tenant por defecto");
    }

    [Fact]
    public void TenantId_Throws_WhenTenantClaimIsNotAValidGuid()
    {
        var identity = new ClaimsIdentity(
            [new Claim(TenantClaimTypes.TenantId, "no-es-un-guid")],
            authenticationType: "Test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var provider = CreateProvider(httpContext);

        var act = () => provider.TenantId;

        act.Should().Throw<TenantResolutionException>();
    }

    [Fact]
    public void TenantId_IgnoresHeadersAndQueryString_OnlyReadsFromUserClaims()
    {
        var otherTenantId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        httpContext.Request.Headers["X-Tenant-Id"] = otherTenantId.ToString();
        httpContext.Request.QueryString = new QueryString($"?tenantId={otherTenantId}");

        var provider = CreateProvider(httpContext);

        provider.TenantId.Should().BeNull(
            "el tenant nunca debe resolverse desde un header o query string controlado por el cliente");
    }
}
