using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Modularity;

/// <summary>
/// Unidad de composición que un proyecto consumidor implementa para agrupar el registro de
/// servicios de una feature cohesiva (p.ej. "ProductosModule", "PagosModule"), en vez de acumular
/// todo en Program.cs. Se descubre e invoca automáticamente vía AddModules(assemblies).
/// </summary>
public interface IFrameworkModule
{
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
