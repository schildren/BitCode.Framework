using BitCode.Framework.Platform.Organization;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Organization.Api;

/// <summary>
/// Módulo del HOST que activa Organization -- vive acá (no en la librería BitCode.Platform.Organization)
/// porque <c>[DependsOn(typeof(InfrastructureModule))]</c> necesita referenciar el
/// <c>InfrastructureModule</c> concreto de este proyecto. Ver
/// <c>OrganizationEndpointRouteBuilderExtensions</c>.
/// </summary>
[DependsOn(typeof(InfrastructureModule))]
public class OrganizationApiModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app) => app.MapOrganizationEndpoints();
}
