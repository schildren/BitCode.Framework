using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;

namespace BitCode.Framework.Shared.Infrastructure.Web.Modularity;

/// <summary>
/// Un IFrameworkModule que además necesita tocar el pipeline HTTP (app.Use...), no solo registrar
/// servicios. UseModules() lo invoca después de que AddModules() ya configuró los servicios,
/// respetando el mismo orden por [DependsOn].
/// </summary>
public interface IWebFrameworkModule : IFrameworkModule
{
    void ConfigureApplication(WebApplication app);
}
