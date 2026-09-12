using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Query;

/// <summary>
/// "API administrativa" de F2-20 (Épica F2-D, entregable "Exponer búsquedas controladas y exportación"):
/// envuelve <see cref="IAuditReader"/> con las tres piezas que el criterio de aceptación
/// ("Acceso auditado y paginado") exige y que una lectura cruda NO puede garantizar por sí sola:
/// <list type="bullet">
/// <item><description><b>Control de acceso</b>: RBAC dedicado (permiso <c>"auditoria.consultar"</c>,
/// ver <see cref="AuditQueryService.RequiredPermission"/>) evaluado vía <c>IPermissionEvaluator</c>
/// (F2-07) -- fail-closed, sin el permiso la consulta se deniega.</description></item>
/// <item><description><b>Aislamiento de tenant</b>: con multi-tenancy habilitada, <see
/// cref="AuditSearchFilter.TenantId"/> debe coincidir exactamente con el tenant del llamador -- nunca
/// una consulta cruzada entre tenants distintos.</description></item>
/// <item><description><b>Acceso auditado</b>: cada llamada (concedida, denegada o fallida) genera su
/// propia <see cref="AuditEntry"/> a través de <see cref="IAuditWriter"/> -- quién consultó/exportó
/// auditoría, con qué filtros, cuándo.</description></item>
/// </list>
/// Es una capa de servicio de aplicación/dominio, no de presentación -- no expone ningún endpoint HTTP
/// concreto (mismo criterio que el resto de <c>Shared.Infrastructure.Security</c>, que no incluye
/// controladores/minimal APIs propios); un proyecto consumidor la invoca desde su propio endpoint
/// administrativo ya protegido/autenticado.
/// </summary>
public interface IAuditQueryService
{
    /// <summary>
    /// Busca <see cref="AuditEntry"/> con <paramref name="filter"/>. Devuelve
    /// <see cref="ErrorType.Forbidden"/> si <paramref name="principal"/> no tiene el permiso RBAC
    /// requerido, o si <see cref="AuditSearchFilter.TenantId"/> no coincide con el tenant del llamador
    /// bajo multi-tenancy habilitada.
    /// </summary>
    Task<Result<PagedResult<AuditEntry>>> SearchAsync(
        ClaimsPrincipal principal, AuditSearchFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exporta el resultado de <paramref name="filter"/> (la página filtrada/paginada, no todo el
    /// histórico) al pipeline WORM ya existente (<see cref="IAuditWormExportPipeline"/>, F2-18) bajo
    /// <paramref name="wormExportKey"/>. Mismo control de acceso y de tenant que <see cref="SearchAsync"/>;
    /// requiere que el proyecto consumidor haya registrado <c>AddSharedAuditWormExport</c> -- devuelve un
    /// <see cref="Result{TValue}"/> fallido (<c>ErrorType.Failure</c>) si no lo hizo, sin lanzar ninguna
    /// excepción de arranque.
    /// </summary>
    Task<Result<WormObjectMetadata>> ExportAsync(
        ClaimsPrincipal principal, AuditSearchFilter filter, string wormExportKey, CancellationToken cancellationToken = default);
}
