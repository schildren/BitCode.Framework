using System.Diagnostics;
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
/// <c>SerilogHostBuilderExtensions.UseSharedSerilog</c>, Shared.Infrastructure.Observability).
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

            await next(context);
        }
    }
}
