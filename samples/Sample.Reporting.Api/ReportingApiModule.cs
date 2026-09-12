using BitCode.Framework.Platform.Reporting;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Reporting.Api;

/// <summary>
/// Módulo del HOST que activa Reporting -- vive acá (no en la librería
/// <c>BitCode.Platform.Reporting</c>) porque <c>[DependsOn(typeof(InfrastructureModule))]</c> necesita
/// referenciar el <c>InfrastructureModule</c> concreto de este proyecto, mismo patrón que
/// <c>TaskInboxApiModule</c> (Fase 6, módulo 7).
/// </summary>
[DependsOn(typeof(InfrastructureModule))]
public class ReportingApiModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app) => app.MapReportingEndpoints();
}
