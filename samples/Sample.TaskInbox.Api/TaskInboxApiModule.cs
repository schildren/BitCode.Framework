using BitCode.Framework.Platform.TaskInbox;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.TaskInbox.Api;

/// <summary>
/// Módulo del HOST que activa Task Inbox -- vive acá (no en la librería
/// <c>BitCode.Platform.TaskInbox</c>) porque <c>[DependsOn(typeof(InfrastructureModule))]</c> necesita
/// referenciar el <c>InfrastructureModule</c> concreto de este proyecto, mismo patrón que
/// <c>WorkflowApiModule</c> (Fase 6, módulo 6).
/// </summary>
[DependsOn(typeof(InfrastructureModule))]
public class TaskInboxApiModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app) => app.MapTaskInboxEndpoints();
}
