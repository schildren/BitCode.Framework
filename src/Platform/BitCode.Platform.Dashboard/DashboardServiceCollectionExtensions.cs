using BitCode.Framework.Platform.Dashboard.Actors;
using BitCode.Framework.Platform.Dashboard.HealthChecks;
using BitCode.Framework.Platform.Dashboard.Metricas;
using BitCode.Framework.Shared.Infrastructure.Http;
using BitCode.Framework.Shared.Infrastructure.Http.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.Dashboard;

/// <summary>
/// Envuelve el cableado de aplicación del módulo Dashboard (Fase 6, módulo 12 -- el ÚLTIMO) -- mismo
/// espíritu que <c>ReportingServiceCollectionExtensions</c>. Un consumidor real llama
/// <c>services.AddSharedPersistence&lt;DashboardDbContext&gt;(connectionString)</c> directamente en su
/// propio <c>InfrastructureModule</c> (ver <c>Sample.Dashboard.Api</c>).
/// </summary>
public static class DashboardServiceCollectionExtensions
{
    /// <param name="reportingBaseUrl">
    /// URL base de la API pública HTTP de Reporting (Fase 6, módulo 11), por ejemplo
    /// <c>"https://reporting.interno.miempresa.com"</c> -- ver el <c>remarks</c> de
    /// <see cref="DashboardOptions.ReportingBaseUrl"/> para por qué se fija acá, a diferencia de la URL
    /// dinámica por conector de Integration Hub.
    /// </param>
    /// <param name="configureHttpResilience">
    /// Configuración opcional de la pipeline de resiliencia HTTP saliente (F1-26) del cliente tipado que
    /// llama a Reporting -- si no se provee, aplican los valores por defecto de
    /// <see cref="HttpResilienceOptions"/>.
    /// </param>
    public static IServiceCollection AddSharedDashboard(
        this IServiceCollection services,
        string reportingBaseUrl,
        Action<HttpResilienceOptions>? configureHttpResilience = null)
    {
        if (string.IsNullOrWhiteSpace(reportingBaseUrl))
        {
            throw new ArgumentException(
                "reportingBaseUrl es obligatorio -- Dashboard necesita la URL base de la API pública de Reporting para resolver la métrica de sus widgets.",
                nameof(reportingBaseUrl));
        }

        services.AddHttpContextAccessor();
        services.TryAddScoped<IDashboardActorContext, HttpContextDashboardActorContext>();

        services.AddResilientHttpClient<DashboardReportingHttpClient>(configureHttpResilience ?? (_ => { }))
            .ConfigureHttpClient(client => client.BaseAddress = new Uri(reportingBaseUrl.TrimEnd('/') + "/"));
        services.TryAddScoped<IReportingMetricSource, ReportingHttpMetricSource>();

        services.AddHealthChecks()
            .AddCheck<DashboardDbContextHealthCheck>("sql-server-dashboard", tags: ["ready"]);

        return services;
    }
}
