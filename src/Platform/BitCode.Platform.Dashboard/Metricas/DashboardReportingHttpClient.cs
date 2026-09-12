namespace BitCode.Framework.Platform.Dashboard.Metricas;

/// <summary>
/// Envoltorio mínimo de <see cref="System.Net.Http.HttpClient"/> registrado como cliente HTTP TIPADO
/// (<c>AddResilientHttpClient&lt;DashboardReportingHttpClient&gt;</c>, F1-26) -- mismo patrón que
/// <c>IntegrationOutboundHttpClient</c> (Fase 6, módulo 9) y <c>VaultSecretProvider</c> (F2-12). A
/// diferencia de <c>IntegrationOutboundHttpClient</c> (URL dinámica, una por conector, resuelta recién en
/// el momento de enviar), acá SÍ se fija <c>BaseAddress</c> en el registro
/// (<see cref="DashboardServiceCollectionExtensions.AddSharedDashboard"/>): Reporting es un servicio único
/// y conocido en tiempo de despliegue, mismo criterio que <c>VaultSecretProvider</c>.
/// </summary>
public sealed class DashboardReportingHttpClient(HttpClient httpClient)
{
    public HttpClient HttpClient { get; } = httpClient;
}
