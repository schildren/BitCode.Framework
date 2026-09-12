using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using Microsoft.AspNetCore.Http;

namespace BitCode.Framework.Platform.Catalogs.Actors;

internal sealed class HttpContextCatalogsActorContext(IHttpContextAccessor httpContextAccessor)
    : ICatalogsActorContext
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
}
