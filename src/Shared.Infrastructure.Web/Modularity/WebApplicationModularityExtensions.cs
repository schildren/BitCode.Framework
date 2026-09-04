using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Web.Modularity;

public static class WebApplicationModularityExtensions
{
    /// <summary>
    /// Invoca ConfigureApplication de todo módulo registrado (vía AddModules) que implemente
    /// IWebFrameworkModule, en el mismo orden por [DependsOn] con el que se configuraron los
    /// servicios. Llamar después de Build(), antes de Run().
    /// </summary>
    public static WebApplication UseModules(this WebApplication app)
    {
        var modules = app.Services.GetServices<IFrameworkModule>();

        foreach (var module in modules)
        {
            if (module is IWebFrameworkModule webModule)
            {
                webModule.ConfigureApplication(app);
            }
        }

        return app;
    }
}
