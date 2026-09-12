using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;

namespace BitCode.Framework.Platform.IntegrationHub.Actors;

/// <summary>
/// Resuelve "quién" ejecuta la operación actual del módulo Integration Hub -- mismo patrón que
/// <c>INotificationsActorContext</c> (Fase 6, módulo 8): <see cref="GetCurrentUserId"/> para distinguir
/// "quién disparó una solicitud puntual" de "quién configuró el conector" (ver el <c>remarks</c> de
/// <c>IntegrationRequest</c> para por qué son dos identidades deliberadamente separadas), y
/// <see cref="GetCurrentActor"/> para auditoría (F2-15) del alta/baja de un conector -- una decisión de
/// configuración con credenciales asociadas, siempre sensible.
/// </summary>
public interface IIntegrationHubActorContext
{
    /// <summary><see langword="null"/> fuera de un pipeline HTTP autenticado (por ejemplo,
    /// <c>IntegrationOutboundProcessorJob</c>, que no corre dentro de un pipeline HTTP).</summary>
    Guid? GetCurrentUserId();

    /// <summary>Actor para auditoría (F2-15) -- <see cref="AuditActorType.User"/> con el
    /// <c>NameIdentifier</c> del request autenticado, o <see cref="AuditActorType.System"/> ("system")
    /// si no hay <c>HttpContext</c>/usuario autenticado.</summary>
    AuditActor GetCurrentActor();

    ClaimsPrincipal? GetCurrentPrincipal();
}
