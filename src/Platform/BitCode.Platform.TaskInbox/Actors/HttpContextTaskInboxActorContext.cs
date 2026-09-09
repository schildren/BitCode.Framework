using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BitCode.Framework.Platform.TaskInbox.Actors;

internal sealed class HttpContextTaskInboxActorContext(IHttpContextAccessor httpContextAccessor) : ITaskInboxActorContext
{
    public Guid? GetCurrentUserId()
    {
        var value = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
