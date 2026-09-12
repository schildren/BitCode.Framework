using BitCode.Framework.Platform.IntegrationHub;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.IntegrationHub.Api;

/// <summary>
/// Módulo del HOST que activa Integration Hub -- vive acá (no en la librería
/// <c>BitCode.Platform.IntegrationHub</c>) porque <c>[DependsOn(typeof(InfrastructureModule))]</c>
/// necesita referenciar el <c>InfrastructureModule</c> concreto de este proyecto, mismo patrón que
/// <c>NotificationsApiModule</c> (Fase 6, módulo 8).
/// </summary>
[DependsOn(typeof(InfrastructureModule))]
public class IntegrationHubApiModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app) => app.MapIntegrationHubEndpoints();
}
