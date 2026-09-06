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

    /// <summary>
    /// F1-15 — criterio de aceptación literal "el tenant no puede sobrescribirse desde el payload":
    /// un usuario autenticado con su propio TenantId en el claim JWT que además envía en el cuerpo
    /// (body) del request un campo <c>tenantId</c> apuntando a otro tenant (por ejemplo, un intento
    /// de acceder a datos de un tenant ajeno) no logra ningún efecto — <see cref="HttpContextTenantProvider"/>
    /// nunca lee <see cref="HttpContext.Request.Body"/>, solo el claim ya validado por el middleware
    /// de autenticación.
    /// </summary>
    [Fact]
    public void TenantId_IgnoresTenantIdInRequestBody_OnlyReadsFromUserClaims()
    {
        var realTenantId = Guid.NewGuid();
        var spoofedTenantIdInBody = Guid.NewGuid();
        var identity = new ClaimsIdentity(
            [new Claim(TenantClaimTypes.TenantId, realTenantId.ToString())],
            authenticationType: "Test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var bodyJson = $$"""{"tenantId":"{{spoofedTenantIdInBody}}","nombre":"cualquier payload"}""";
        var bodyBytes = System.Text.Encoding.UTF8.GetBytes(bodyJson);
        httpContext.Request.Body = new MemoryStream(bodyBytes);
        httpContext.Request.ContentLength = bodyBytes.Length;
        httpContext.Request.ContentType = "application/json";

        var provider = CreateProvider(httpContext);

        provider.TenantId.Should().Be(realTenantId,
            "el TenantId resuelto debe ser siempre el del claim JWT autenticado, nunca el que el cliente " +
            "haya podido incluir en el cuerpo del request");
        provider.TenantId.Should().NotBe(spoofedTenantIdInBody);
    }
}
