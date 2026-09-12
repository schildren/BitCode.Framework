using Microsoft.AspNetCore.Builder;

namespace BitCode.Framework.Shared.Infrastructure.Web.MultiTenancy;

public static class TenantContextApplicationBuilderExtensions
{
    /// <summary>
    /// Registra <see cref="TenantLogEnrichmentMiddleware"/> (F1-15): a partir de este punto del
    /// pipeline, todos los logs estructurados emitidos durante el request incluyen la propiedad
    /// <c>TenantId</c> sin que cada log statement la agregue a mano. Llamar después de
    /// <c>UseAuthentication()</c>/<c>UseAuthorization()</c> (necesita el usuario ya autenticado para
    /// resolver el claim de tenant) y antes de mapear los endpoints.
    /// </summary>
    public static IApplicationBuilder UseTenantContextLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantLogEnrichmentMiddleware>();
}
