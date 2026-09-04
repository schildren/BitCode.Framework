using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Infrastructure.Persistence;
using BitCode.Framework.Shared.Infrastructure.Web;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Api;

/// <summary>
/// Envuelve los AddSharedX&lt;T&gt;() del framework con los tipos concretos de este proyecto — el
/// patrón documentado en la Fase 5 (Shared.Modularity): IFrameworkModule no reemplaza estos
/// extension methods, los agrupa para que el resto de los módulos de feature (ProductosModule)
/// solo declaren [DependsOn(typeof(InfrastructureModule))] sin repetir la configuración.
/// </summary>
public class InfrastructureModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");

        services.AddSharedPersistence<SampleDbContext>(connectionString);
        services.AddSharedApplication(typeof(InfrastructureModule).Assembly);
        services.AddSharedExceptionHandling();
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.UseExceptionHandler();
    }
}
