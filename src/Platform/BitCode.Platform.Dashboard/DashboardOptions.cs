namespace BitCode.Framework.Platform.Dashboard;

/// <summary>
/// Opciones del módulo Dashboard -- sección de configuración <c>"Dashboard"</c>.
/// </summary>
/// <remarks>
/// <see cref="ReportingBaseUrl"/> es la URL base de la API pública HTTP de Reporting (Fase 6, módulo 11,
/// <c>GET /api/v1/reporting/workflow-instancias/promedio-duracion</c>) -- NO una cadena de conexión ni una
/// referencia a <c>ReportingDbContext</c>: este módulo nunca accede a la base de datos de Reporting
/// directamente (ver el <c>remarks</c> del <c>csproj</c> de este proyecto). Se resuelve una única vez, en
/// el momento de registrar <see cref="Metricas.DashboardReportingHttpClient"/>
/// (<see cref="DashboardServiceCollectionExtensions.AddSharedDashboard"/>) -- mismo criterio que
/// <c>VaultSecretProviderOptions.Address</c> (F2-12): la URL de un servicio conocido en tiempo de
/// despliegue, fijada como <c>HttpClient.BaseAddress</c> al registrar el cliente tipado, a diferencia de
/// la URL DINÁMICA por conector de Integration Hub (Fase 6, módulo 9), resuelta recién en el momento de
/// enviar.
/// </remarks>
public sealed class DashboardOptions
{
    public string ReportingBaseUrl { get; set; } = string.Empty;
}
