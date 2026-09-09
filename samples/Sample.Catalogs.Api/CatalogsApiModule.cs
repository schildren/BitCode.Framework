using BitCode.Framework.Platform.Catalogs;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Catalogs.Api;

/// <summary>
/// Módulo del HOST que activa Catalogs and Parameters -- vive acá (no en la librería
/// <c>BitCode.Platform.Catalogs</c>) porque <c>[DependsOn(typeof(InfrastructureModule))]</c> necesita
/// referenciar el <c>InfrastructureModule</c> concreto de este proyecto, mismo patrón que
/// <c>OrganizationApiModule</c> (Fase 6, módulo 2).
/// </summary>
[DependsOn(typeof(InfrastructureModule))]
public class CatalogsApiModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app) => app.MapCatalogsEndpoints();
}
