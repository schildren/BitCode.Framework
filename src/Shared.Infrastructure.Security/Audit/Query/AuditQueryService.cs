using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Query;

/// <summary>
/// Implementación por defecto de <see cref="IAuditQueryService"/> (F2-20, Épica F2-D). Registrada por
/// <see cref="AuditQueryServiceCollectionExtensions.AddSharedAuditQuery"/>.
/// <para>
/// <b>Por qué <c>IPermissionEvaluator</c> (RBAC puro, F2-07) y no <c>IAuthorizationPolicyEvaluator</c>
/// (RBAC+ABAC+step-up combinado, F2-08/F2-10)</b>: usar el evaluador combinado exigiría que TODO proyecto
/// que quiera consultar auditoría registre también el módulo ABAC completo (<c>AddSharedAbacAuthorization</c>)
/// aunque no lo necesite para nada más -- una dependencia innecesaria para el entregable mínimo de F2-20
/// ("búsquedas controladas y exportación", no "step-up para consultar auditoría"). Un proyecto que SÍ
/// quiera exigir step-up (F2-10) para <c>"auditoria.consultar"</c>/<c>"auditoria.exportar"</c> puede
/// envolver <see cref="IAuditQueryService"/> con su propio decorador que invoque primero
/// <c>IAuthorizationPolicyEvaluator.EvaluateAsync</c> sobre un <c>AbacResource("auditoria")</c> antes de
/// delegar -- reutilizando F2-10 sin que este módulo lo imponga a todos.
/// </para>
/// <para>
/// <b>Cómo se evita la recursión de auditar la propia consulta de auditoría</b>: <see cref="WriteAccessAuditAsync"/>
/// escribe SIEMPRE a través de <see cref="IAuditWriter.WriteAsync"/> directamente, nunca a través de
/// <see cref="SearchAsync"/>/<see cref="ExportAsync"/> de esta misma clase -- auditar el acceso a auditoría
/// no dispara, en ningún camino de código, una nueva consulta.
/// </para>
/// </summary>
public sealed class AuditQueryService(
    IAuditReader reader,
    IAuditWriter auditWriter,
    IPermissionEvaluator permissionEvaluator,
    ITenantContext tenantContext,
    IAuditWormExportPipeline? wormExportPipeline = null) : IAuditQueryService
{
    /// <summary>
    /// Permiso RBAC dedicado (F2-07, convención <c>"{entidad}.{accion}"</c>) requerido tanto para
    /// <see cref="SearchAsync"/> como para <see cref="ExportAsync"/> -- exportar es, en esencia, buscar más
    /// persistir el resultado ya autorizado; no se introduce un segundo permiso separado para mantener el
    /// entregable mínimo (ver <c>docs/guia-auditoria-inmutable.md</c>, sección F2-20, para la justificación
    /// completa y cómo un proyecto puede diferenciarlos si lo necesita).
    /// </summary>
    public const string RequiredPermission = "auditoria.consultar";

    /// <summary>
    /// Permiso RBAC ADICIONAL (fix post-revisión de arquitectura de F2-20, Hallazgo 2, Alto), más
    /// restrictivo que <see cref="RequiredPermission"/>, exigido ÚNICAMENTE cuando el llamador no tiene
    /// ningún <c>TenantId</c> resuelto (<c>ITenantContext.TenantId == null</c> con
    /// <c>IsMultiTenancyEnabled == true</c> -- un job en background o una cuenta de servicio sin
    /// <c>HttpContext</c>/claim de tenant, ver <c>HttpContextTenantProvider</c>) Y el filtro tampoco
    /// restringe <c>TenantId</c> (<c>AuditSearchFilter.TenantId == null</c>, que para <see cref="IAuditReader"/>
    /// significa "sin restricción de tenant"). Sin este permiso adicional, esa combinación se deniega
    /// fail-closed aunque el llamador ya tenga <see cref="RequiredPermission"/> -- ver
    /// <c>docs/guia-auditoria-inmutable.md</c>, sección F2-20, para el detalle completo del hueco cerrado.
    /// </summary>
    public const string CrossTenantPermission = "auditoria.consultar.todostenants";

    private const string SearchAction = "auditoria.consultar";
    private const string ExportAction = "auditoria.exportar";
    private const string DeniedReason = "rbac:missing-permission:" + RequiredPermission;
    private const string TenantMismatchReason = "auditoria:tenant-scope-mismatch";
    private const string UnresolvedTenantReason = "auditoria:tenant-no-resuelto-sin-restriccion";

    public async Task<Result<PagedResult<AuditEntry>>> SearchAsync(
        ClaimsPrincipal principal, AuditSearchFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(filter);

        var accessCheck = await CheckAccessAsync(principal, filter, SearchAction, cancellationToken);
        if (accessCheck is not null)
        {
            return Result.Failure<PagedResult<AuditEntry>>(accessCheck);
        }

        var searchResult = await reader.SearchAsync(filter, cancellationToken);

        await WriteAccessAuditAsync(
            principal, filter, SearchAction,
            searchResult.IsSuccess ? AuditOutcome.Success : AuditOutcome.Error,
            searchResult.IsSuccess ? null : searchResult.Error.Code,
            cancellationToken);

        return searchResult;
    }

    public async Task<Result<WormObjectMetadata>> ExportAsync(
        ClaimsPrincipal principal, AuditSearchFilter filter, string wormExportKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentException.ThrowIfNullOrWhiteSpace(wormExportKey);

        var accessCheck = await CheckAccessAsync(principal, filter, ExportAction, cancellationToken);
        if (accessCheck is not null)
        {
            return Result.Failure<WormObjectMetadata>(accessCheck);
        }

        if (wormExportPipeline is null)
        {
            var error = Error.Failure(
                "Auditoria.ExportacionNoConfigurada",
                "AddSharedAuditWormExport (F2-18) debe registrarse para poder exportar resultados de auditoría.");
            await WriteAccessAuditAsync(principal, filter, ExportAction, AuditOutcome.Error, error.Code, cancellationToken);
            return Result.Failure<WormObjectMetadata>(error);
        }

        var searchResult = await reader.SearchAsync(filter, cancellationToken);
        if (searchResult.IsFailure)
        {
            await WriteAccessAuditAsync(principal, filter, ExportAction, AuditOutcome.Error, searchResult.Error.Code, cancellationToken);
            return Result.Failure<WormObjectMetadata>(searchResult.Error);
        }

        // Exporta EXACTAMENTE la página ya filtrada/paginada devuelta por la búsqueda -- no todo el
        // histórico que matchea el filtro (ver AuditSearchFilter/IAuditQueryService). Reutiliza el pipeline
        // WORM de F2-18 tal cual, sin reinventar el mecanismo de exportación.
        var exportRequest = new AuditWormExportRequest(wormExportKey, searchResult.Value.Items);
        var exportResult = await wormExportPipeline.ExportAsync(exportRequest, cancellationToken);

        await WriteAccessAuditAsync(
            principal, filter, ExportAction,
            exportResult.IsSuccess ? AuditOutcome.Success : AuditOutcome.Error,
            exportResult.IsSuccess ? null : exportResult.Error.Code,
            cancellationToken);

        return exportResult;
    }

    /// <summary>
    /// Evalúa permiso RBAC + aislamiento de tenant y, si alguno falla, escribe la entrada de auditoría de
    /// acceso DENEGADO correspondiente. Devuelve <see langword="null"/> cuando el acceso está permitido
    /// (el llamador continúa); un <see cref="Error"/> no nulo cuando debe cortar y devolverlo como
    /// <see cref="Result{TValue}"/> fallido.
    /// </summary>
    private async Task<Error?> CheckAccessAsync(
        ClaimsPrincipal principal, AuditSearchFilter filter, string action, CancellationToken cancellationToken)
    {
        var effectivePermissions = await permissionEvaluator.EvaluateAsync(principal, cancellationToken);

        if (tenantContext.IsMultiTenancyEnabled)
        {
            if (tenantContext.TenantId is null && filter.TenantId is null)
            {
                // Fix post-revisión de arquitectura de F2-20 (Hallazgo 2, Alto): tenantContext.TenantId es
                // Guid? y puede ser null incluso con IsMultiTenancyEnabled == true (un job en background sin
                // HttpContext, o una cuenta de servicio sin claim de tenant -- ver HttpContextTenantProvider).
                // La comparación "filter.TenantId != tenantContext.TenantId" de más abajo NO detecta ningún
                // mismatch cuando AMBOS son null ("null != null" es false) -- sin este chequeo explícito, un
                // llamador sin tenant resuelto podría pedir filter.TenantId = null (que para IAuditReader
                // significa "sin restricción de tenant") y obtener una búsqueda/exportación CROSS-TENANT con
                // solo el permiso base RequiredPermission. Se exige acá un permiso adicional, más
                // restrictivo, para ese caso deliberado de plataforma -- fail-closed en cualquier otro caso.
                if (!effectivePermissions.HasPermission(CrossTenantPermission))
                {
                    await WriteAccessAuditAsync(principal, filter, action, AuditOutcome.Denied, UnresolvedTenantReason, cancellationToken);
                    return Error.Forbidden(
                        "Auditoria.TenantNoResuelto",
                        "El llamador no tiene un tenant resuelto y el filtro no restringe TenantId. Se requiere " +
                        $"el permiso '{CrossTenantPermission}' para una consulta/exportación de auditoría sin " +
                        "límite de tenant.");
                }
            }
            else if (filter.TenantId != tenantContext.TenantId)
            {
                await WriteAccessAuditAsync(principal, filter, action, AuditOutcome.Denied, TenantMismatchReason, cancellationToken);
                return Error.Forbidden(
                    "Auditoria.TenantNoCoincide",
                    "El filtro de búsqueda debe restringirse al tenant del llamador.");
            }
        }

        if (!effectivePermissions.HasPermission(RequiredPermission))
        {
            await WriteAccessAuditAsync(principal, filter, action, AuditOutcome.Denied, DeniedReason, cancellationToken);
            return Error.Forbidden(
                "Auditoria.PermisoRequerido",
                $"Se requiere el permiso '{RequiredPermission}' para consultar/exportar auditoría.");
        }

        return null;
    }

    private async Task WriteAccessAuditAsync(
        ClaimsPrincipal principal, AuditSearchFilter filter, string action, AuditOutcome outcome, string? reason,
        CancellationToken cancellationToken)
    {
        var actorId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.Identity?.Name
            ?? "unknown";
        var actor = new AuditActor(actorId, AuditActorType.User);

        var request = new AuditEntryRequest(
            actor: actor,
            tenantId: filter.TenantId,
            action: action,
            resource: new AuditResource("auditoria"),
            outcome: outcome,
            reason: reason,
            metadata: BuildFilterMetadata(filter));

        // Escritura DIRECTA vía IAuditWriter -- nunca a través de SearchAsync/ExportAsync de esta misma
        // clase (ver el comentario de tipo, "Cómo se evita la recursión"). Un fallo transitorio de
        // IAuditWriter no bloquea ni altera la decisión/resultado ya calculado -- mismo criterio que
        // AuditingAuthorizationPolicyEvaluator (F2-10).
        _ = await auditWriter.WriteAsync(request, cancellationToken);
    }

    private static IReadOnlyDictionary<string, string?> BuildFilterMetadata(AuditSearchFilter filter) => new Dictionary<string, string?>
    {
        ["page"] = filter.Page.Page.ToString(),
        ["pageSize"] = filter.Page.PageSize.ToString(),
        ["fromUtc"] = filter.FromUtc?.ToString("O"),
        ["toUtc"] = filter.ToUtc?.ToString("O"),
        ["actorId"] = filter.ActorId,
        ["action"] = filter.Action,
        ["outcome"] = filter.Outcome?.ToString(),
        ["resourceType"] = filter.ResourceType,
    };
}
