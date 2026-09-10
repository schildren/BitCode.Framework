using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BitCode.Framework.Platform.Dashboard.Actors;

internal sealed class HttpContextDashboardActorContext(IHttpContextAccessor httpContextAccessor) : IDashboardActorContext
{
    public Guid? GetCurrentUserId()
    {
        var value = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
