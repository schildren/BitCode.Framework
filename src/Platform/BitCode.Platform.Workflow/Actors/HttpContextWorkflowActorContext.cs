using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using Microsoft.AspNetCore.Http;

namespace BitCode.Framework.Platform.Workflow.Actors;

internal sealed class HttpContextWorkflowActorContext(IHttpContextAccessor httpContextAccessor)
    : IWorkflowActorContext
{
    public AuditActor GetCurrentActor()
    {
        var principal = GetCurrentPrincipal();
        var userId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return userId is null
            ? new AuditActor("system", AuditActorType.System)
            : new AuditActor(userId, AuditActorType.User);
    }

    public ClaimsPrincipal? GetCurrentPrincipal() => httpContextAccessor.HttpContext?.User;

    public Guid? GetCurrentUserId()
    {
        var value = GetCurrentPrincipal()?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
