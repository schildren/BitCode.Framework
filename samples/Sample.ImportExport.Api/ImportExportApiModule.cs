using BitCode.Framework.Platform.ImportExport;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.ImportExport.Api;

/// <summary>
/// Módulo del HOST que activa Import and Export -- vive acá (no en la librería
/// <c>BitCode.Platform.ImportExport</c>) porque <c>[DependsOn(typeof(InfrastructureModule))]</c> necesita
/// referenciar el <c>InfrastructureModule</c> concreto de este proyecto, mismo patrón que
/// <c>IntegrationHubApiModule</c> (Fase 6, módulo 9).
/// </summary>
[DependsOn(typeof(InfrastructureModule))]
public class ImportExportApiModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app) => app.MapImportExportEndpoints();
}
