using BitCode.Framework.Platform.Workflow;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Workflow.Api;

/// <summary>
/// Módulo del HOST que activa Workflow -- vive acá (no en la librería <c>BitCode.Platform.Workflow</c>)
/// porque <c>[DependsOn(typeof(InfrastructureModule))]</c> necesita referenciar el
/// <c>InfrastructureModule</c> concreto de este proyecto, mismo patrón que <c>CatalogsApiModule</c>
/// (Fase 6, módulo 3).
/// </summary>
[DependsOn(typeof(InfrastructureModule))]
public class WorkflowApiModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app) => app.MapWorkflowEndpoints();
}
