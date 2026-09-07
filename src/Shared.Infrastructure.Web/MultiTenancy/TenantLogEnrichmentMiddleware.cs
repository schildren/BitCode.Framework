using System.Diagnostics;
using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace BitCode.Framework.Shared.Infrastructure.Web.MultiTenancy;

/// <summary>
/// Middleware de propagación/logging del F1-15: resuelve el <c>TenantId</c> del request actual (vía
/// <see cref="ITenantContext"/>, que a su vez memoiza lo que devuelva <see cref="ITenantProvider"/>
/// — típicamente <c>HttpContextTenantProvider</c>, F1-12) y lo empuja al contexto estructurado de
/// Serilog (<see cref="LogContext"/>) para el resto del pipeline. Con esto, cualquier
/// <c>ILogger.LogInformation(...)</c> emitido durante el procesamiento del request incluye
/// automáticamente la propiedad <c>TenantId</c> — sin que cada call site tenga que agregarla a
/// mano — siempre que el host haya configurado Serilog con <c>.Enrich.FromLogContext()</c> (ver
/// <c>SerilogHostBuilderExtensions.UseSharedSerilog</c>, Shared.Infrastructure.Observability). Desde
/// el cierre de pendiente de F4-10 (<c>docs/guia-otel-collector.md</c> sección 2), también etiqueta el
/// <see cref="Activity"/>/traza OTel vigente con <c>tenant_id</c> y <c>user_id</c> (el claim
/// <see cref="ClaimTypes.NameIdentifier"/> del usuario autenticado — un identificador técnico, nunca
/// nombre/email, mismo criterio de redacción de PII que el resto del repositorio) — dos de los
/// atributos de "telemetría mínima" que exige la Fase 4 del Plan Maestro.
/// </summary>
/// <remarks>
/// Registrar con <c>UseTenantContextLogging</c> (<see cref="TenantContextApplicationBuilderExtensions"/>) después de
/// <c>UseAuthentication()</c>/<c>UseAuthorization()</c> (el <c>TenantId</c> se resuelve desde el
/// claim JWT del usuario ya autenticado) y antes de los endpoints. La propiedad se retira del
/// contexto automáticamente al salir del <see langword="using"/> — no se filtra a requests
/// posteriores ni a otros hilos.
/// </remarks>
public sealed class TenantLogEnrichmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        using (LogContext.PushProperty("TenantId", tenantContext.TenantId))
        {
            // F4-10: además del log estructurado (arriba), etiqueta el Activity/traza OTel vigente del
            // request con "tenant_id" -- uno de los atributos de "telemetría mínima" exigidos por la
            // Fase 4 del Plan Maestro. Sin esto, un collector centralizado (F4-10) recibe trazas
            // correlacionadas por trace_id/span_id pero sin forma de filtrar/agrupar por tenant en el
            // backend de observabilidad. SetTag es un no-op seguro si no hay Activity actual (p. ej.
            // AddSharedObservability no está configurado, o el sampler descartó este request) o si
            // TenantId es null (multitenancy deshabilitada) -- no falla el request en ningún caso.
            Activity.Current?.SetTag("tenant_id", tenantContext.TenantId);

            // Cierre de pendiente F4-10 (ver docs/guia-otel-collector.md sección 2): "user_id"/workload
            // identity de la "telemetría mínima" de la Fase 4. Mismo criterio de identificador TÉCNICO,
            // no PII, que ya usa el resto del repositorio para "actorId" (AuditingAuthorizationPolicyEvaluator,
            // AuditQueryService, PermissionEvaluator): ClaimTypes.NameIdentifier, el claim "sub" del
            // token JWT ya validado (JwtTokenGenerator lo emite como el Id técnico -- GUID -- del
            // ApplicationUser, nunca nombre/email). No se agrega un middleware nuevo -- este ya corre
            // después de UseAuthentication()/UseAuthorization() (ver UseTenantContextLogging) y ya
            // resuelve el usuario autenticado del mismo HttpContext, así que sumar este tag acá es el
            // "cambio pequeño y acotado" documentado como pendiente, no un mecanismo nuevo. SetTag con
            // null (anónimo/sin autenticar) es un no-op seguro, igual que "tenant_id" arriba.
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Activity.Current?.SetTag("user_id", userId);

            await next(context);
        }
    }
}
