using BitCode.Framework.Platform.Notifications;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Notifications.Api;

/// <summary>
/// Módulo del HOST que activa Notifications -- vive acá (no en la librería
/// <c>BitCode.Platform.Notifications</c>) porque <c>[DependsOn(typeof(InfrastructureModule))]</c>
/// necesita referenciar el <c>InfrastructureModule</c> concreto de este proyecto, mismo patrón que
/// <c>TaskInboxApiModule</c> (Fase 6, módulo 7).
/// </summary>
[DependsOn(typeof(InfrastructureModule))]
public class NotificationsApiModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app) => app.MapNotificationsEndpoints();
}
