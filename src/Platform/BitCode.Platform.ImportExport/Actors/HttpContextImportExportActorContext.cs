using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using Microsoft.AspNetCore.Http;

namespace BitCode.Framework.Platform.ImportExport.Actors;

internal sealed class HttpContextImportExportActorContext(IHttpContextAccessor httpContextAccessor)
    : IImportExportActorContext
{
    public Guid? GetCurrentUserId()
    {
        var value = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public AuditActor GetCurrentActor()
    {
        var userId = GetCurrentUserId();
        return userId is null
            ? new AuditActor("system", AuditActorType.System)
            : new AuditActor(userId.Value.ToString(), AuditActorType.User);
    }

    public ClaimsPrincipal? GetCurrentPrincipal() => httpContextAccessor.HttpContext?.User;
}
