using BitCode.Framework.Platform.Identity;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.IdentityAdmin.Api;

/// <summary>
/// Módulo del HOST que activa Identity Administration -- vive acá (no en la librería
/// BitCode.Platform.Identity) porque <c>[DependsOn(typeof(InfrastructureModule))]</c> necesita
/// referenciar el <c>InfrastructureModule</c> concreto de este proyecto, que la librería no puede
/// conocer en tiempo de compilación. Ver <c>IdentityAdministrationEndpointRouteBuilderExtensions</c>.
/// </summary>
[DependsOn(typeof(InfrastructureModule))]
public class IdentityAdminModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app) => app.MapIdentityAdministrationEndpoints();
}
