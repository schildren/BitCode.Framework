using BitCode.Framework.Platform.Documents;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Documents.Api;

/// <summary>
/// Módulo del HOST que activa Documents -- vive acá (no en la librería <c>BitCode.Platform.Documents</c>)
/// porque <c>[DependsOn(typeof(InfrastructureModule))]</c> necesita referenciar el
/// <c>InfrastructureModule</c> concreto de este proyecto, mismo patrón que <c>FeatureManagementApiModule</c>
/// (Fase 6, módulo 4).
/// </summary>
[DependsOn(typeof(InfrastructureModule))]
public class DocumentsApiModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app) => app.MapDocumentsEndpoints();
}
