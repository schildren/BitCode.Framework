using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Gateway.RequestLimits;

/// <summary>
/// Registro de <see cref="GatewayRequestLimitsOptions"/> (F4-09) -- leída de la sección "RequestLimits",
/// configurable por el proyecto consumidor, no hardcodeada (mismo patrón que
/// <c>AddGatewayRateLimiting</c>, F4-08).
/// </summary>
public static class GatewayRequestLimitsServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayRequestLimits(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GatewayRequestLimitsOptions>(
            configuration.GetSection(GatewayRequestLimitsOptions.SectionName));

        return services;
    }
}
