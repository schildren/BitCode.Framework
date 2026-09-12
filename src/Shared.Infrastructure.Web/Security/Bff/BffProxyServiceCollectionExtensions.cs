using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Web.Security.Bff;

/// <summary>
/// Registra el proxy YARP del BFF (F2-03) hacia las APIs protegidas. La topología de rutas/clusters
/// (a qué host reenviar cada ruta) se declara de forma estándar de YARP en la sección de configuración
/// <c>"ReverseProxy"</c> (<c>ReverseProxy:Routes</c>/<c>ReverseProxy:Clusters</c>) -- este método no
/// impone ninguna ruta fija, para que cada proyecto consumidor decida cuántas APIs expone detrás del
/// BFF y bajo qué prefijos.
/// </summary>
public static class BffProxyServiceCollectionExtensions
{
    /// <summary>
    /// <c>services.AddReverseProxy().LoadFromConfig(configuration.GetSection("ReverseProxy"))</c> más
    /// <see cref="BffAccessTokenRequestTransform"/> aplicado a TODAS las rutas configuradas: cada
    /// request que YARP reenvía a una API downstream lleva el access token de la sesión server-side del
    /// usuario, sin que cada ruta tenga que declararlo individualmente ni el navegador participe.
    /// </summary>
    public static IServiceCollection AddSharedBffProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"))
            .AddTransforms(transformBuilderContext =>
                transformBuilderContext.RequestTransforms.Add(new BffAccessTokenRequestTransform()));

        return services;
    }
}
