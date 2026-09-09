using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;

namespace BitCode.Framework.Platform.ImportExport.Actors;

/// <summary>
/// Resuelve "quién" ejecuta la operación actual del módulo Import and Export -- mismo patrón que
/// <c>IIntegrationHubActorContext</c> (Fase 6, módulo 9)/<c>INotificationsActorContext</c> (módulo 8).
/// <see cref="GetCurrentUserId"/> identifica quién inició un <c>ImportJob</c>/<c>ExportJob</c> puntual --
/// deliberadamente separado de cualquier otra identidad futura (por ejemplo, quién registró un
/// <c>IImportRowHandler</c>/<c>IExportDataSource</c> del lado del host, que es una decisión de despliegue,
/// no una acción de un usuario en tiempo de ejecución) -- mismo criterio de no conflacionar identidades
/// distintas que corrigió Task Inbox (hallazgo Crítico, 2026-09-09).
/// </summary>
public interface IImportExportActorContext
{
    /// <summary><see langword="null"/> fuera de un pipeline HTTP autenticado (por ejemplo,
    /// <c>ImportBatchProcessorJob</c>/<c>ExportBatchProcessorJob</c>, que no corren dentro de un pipeline
    /// HTTP).</summary>
    Guid? GetCurrentUserId();

    /// <summary>Actor para auditoría (F2-15) -- <see cref="AuditActorType.User"/> con el
    /// <c>NameIdentifier</c> del request autenticado, o <see cref="AuditActorType.System"/> ("system")
    /// si no hay <c>HttpContext</c>/usuario autenticado.</summary>
    AuditActor GetCurrentActor();

    ClaimsPrincipal? GetCurrentPrincipal();
}
