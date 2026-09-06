using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;

/// <summary>
/// Decorador de <see cref="IAuthorizationPolicyEvaluator"/> que cierra el pendiente explícito dejado por
/// F2-15 (<c>docs/guia-auditoria-inmutable.md</c>, sección "Pendiente explícito"): cablea
/// <see cref="IAuditWriter.WriteAsync"/> sobre las operaciones privilegiadas de F2-10 (step-up
/// authentication, segregación de funciones), tanto cuando la decisión final resulta concedida como
/// denegada.
/// <para>
/// Delibaradamente NO audita toda evaluación ABAC (F2-08) -- solo aquella cuyo (tipo de recurso, acción)
/// esté protegido por al menos una política de operaciones privilegiadas configurada
/// (<see cref="PrivilegedOperationsOptions.StepUpRequirements"/>, <see
/// cref="PrivilegedOperationsOptions.MakerCheckerRules"/>, o al menos un elemento en <see
/// cref="PrivilegedOperationsOptions.MutuallyExclusivePermissions"/>, que aplica a toda evaluación por ser
/// una restricción de identidad -- ver <see cref="SegregationOfDutiesAbacRule"/>). Auditar
/// incondicionalmente cada evaluación ABAC genérica generaría ruido sobre operaciones que el propio Plan
/// Maestro no clasifica como críticas (gate de salida de Fase 2, "operaciones críticas generan auditoría
/// íntegra").
/// </para>
/// <para>
/// SIEMPRE delega la decisión real al evaluador combinado ya registrado (RBAC + ABAC + privilegiadas) --
/// nunca la recalcula ni la altera. Un fallo al escribir la entrada de auditoría
/// (<see cref="IAuditWriter.WriteAsync"/> devuelve un <c>Result</c> fallido) no bloquea ni cambia la
/// decisión de autorización ya tomada: bloquear una operación de negocio legítima porque el almacenamiento
/// de auditoría tuvo un problema transitorio sería un nuevo modo de falla peor que el que se intenta
/// resolver. Registrado como decorador (no como pipeline paralelo) por
/// <see cref="PrivilegedOperationsServiceCollectionExtensions.AddSharedPrivilegedOperationsPolicies"/>.
/// </para>
/// </summary>
public sealed class AuditingAuthorizationPolicyEvaluator(
    IAuthorizationPolicyEvaluator inner,
    IAuditWriter auditWriter,
    ITenantContext tenantContext,
    IOptions<PrivilegedOperationsOptions> options) : IAuthorizationPolicyEvaluator
{
    public async Task<AbacDecision> EvaluateAsync(
        ClaimsPrincipal principal,
        AbacResource resource,
        string action,
        AbacContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var decision = await inner.EvaluateAsync(principal, resource, action, context, cancellationToken);

        if (!IsPrivilegedOperation(resource.Type, action))
        {
            return decision;
        }

        await WriteAuditEntryAsync(principal, resource, action, decision, cancellationToken);

        return decision;
    }

    /// <summary>
    /// Determina si (<paramref name="resourceType"/>, <paramref name="action"/>) está protegido por al
    /// menos una política de operaciones privilegiadas configurada -- misma semántica de matching
    /// (<c>"*"</c> como comodín, comparación Ordinal) que <see cref="StepUpAbacRule.AppliesTo"/> y
    /// <see cref="SegregationOfDutiesAbacRule.AppliesTo"/>, para que "esta evaluación es una operación
    /// privilegiada" nunca diverja de "esta evaluación efectivamente pasó por una de esas dos reglas".
    /// </summary>
    private bool IsPrivilegedOperation(string resourceType, string action)
    {
        var privilegedOptions = options.Value;

        return privilegedOptions.MutuallyExclusivePermissions.Count > 0 ||
            privilegedOptions.StepUpRequirements.Any(r => Matches(r.ResourceType, r.Action, resourceType, action)) ||
            privilegedOptions.MakerCheckerRules.Any(r => Matches(r.ResourceType, r.Action, resourceType, action));
    }

    private async Task WriteAuditEntryAsync(
        ClaimsPrincipal principal,
        AbacResource resource,
        string action,
        AbacDecision decision,
        CancellationToken cancellationToken)
    {
        var actorId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.Identity?.Name
            ?? "unknown";
        var actor = new AuditActor(actorId, AuditActorType.User);

        var tenantId = tenantContext.IsMultiTenancyEnabled ? tenantContext.TenantId : null;
        var auditAction = $"{resource.Type}.{action}";
        var auditResource = new AuditResource(resource.Type);
        var outcome = decision.Allowed ? AuditOutcome.Success : AuditOutcome.Denied;
        var reason = decision.Allowed ? null : decision.Reason;
        var metadata = new Dictionary<string, string?>
        {
            ["abacDecisionReason"] = decision.Reason,
        };

        var request = new AuditEntryRequest(
            actor: actor,
            tenantId: tenantId,
            action: auditAction,
            resource: auditResource,
            outcome: outcome,
            reason: reason,
            metadata: metadata);

        // Resultado de la escritura deliberadamente descartado -- ver la documentación de esta clase: un
        // fallo transitorio de IAuditWriter nunca bloquea ni altera la decisión de autorización ya
        // devuelta al llamador. Un proyecto que necesite alertar sobre ese fallo lo hace en su propia
        // implementación de IAuditWriter (por ejemplo, logueando antes de devolver Result.Failure).
        _ = await auditWriter.WriteAsync(request, cancellationToken);
    }

    private static bool Matches(string patternResourceType, string patternAction, string resourceType, string action) =>
        (patternResourceType == "*" || string.Equals(patternResourceType, resourceType, StringComparison.Ordinal)) &&
        (patternAction == "*" || string.Equals(patternAction, action, StringComparison.Ordinal));
}
