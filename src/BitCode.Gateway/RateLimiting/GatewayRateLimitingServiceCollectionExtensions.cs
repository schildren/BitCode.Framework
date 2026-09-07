using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Gateway.RateLimiting;

/// <summary>
/// Rate limiting nativo de ASP.NET Core (F4-08) -- política fixed window global (nombre de política
/// <see cref="PolicyName"/>) leída de la sección de configuración "RateLimiting"
/// (<see cref="GatewayRateLimitingOptions"/>), aplicada explícitamente sobre el proxy YARP en
/// Program.cs (<c>.RequireRateLimiting(GatewayRateLimitingServiceCollectionExtensions.PolicyName)</c>).
/// </summary>
public static class GatewayRateLimitingServiceCollectionExtensions
{
    public const string PolicyName = "gateway";

    public static IServiceCollection AddGatewayRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GatewayRateLimitingOptions>(
            configuration.GetSection(GatewayRateLimitingOptions.SectionName));

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            rateLimiterOptions.AddFixedWindowLimiter(PolicyName, fixedWindowOptions =>
            {
                var gatewayOptions = configuration
                    .GetSection(GatewayRateLimitingOptions.SectionName)
                    .Get<GatewayRateLimitingOptions>() ?? new GatewayRateLimitingOptions();

                fixedWindowOptions.PermitLimit = gatewayOptions.PermitLimit;
                fixedWindowOptions.Window = TimeSpan.FromSeconds(gatewayOptions.WindowSeconds);
                fixedWindowOptions.QueueLimit = gatewayOptions.QueueLimit;
                fixedWindowOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
            });
        });

        return services;
    }
}
