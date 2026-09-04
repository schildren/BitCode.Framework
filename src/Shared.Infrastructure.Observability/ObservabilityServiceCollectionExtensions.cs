using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BitCode.Framework.Shared.Infrastructure.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Registra tracing (ASP.NET Core + HttpClient) y métricas de OpenTelemetry. Requiere la
    /// sección "OpenTelemetry" (ServiceName obligatorio, OtlpEndpoint opcional) — sin
    /// OtlpEndpoint configurado, las trazas/métricas se generan igual mediante los exportadores
    /// pero no se envían a ningún collector.
    /// </summary>
    public static IServiceCollection AddSharedObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(OpenTelemetryOptions.SectionName);
        var options = section.Get<OpenTelemetryOptions>()
            ?? throw new InvalidOperationException(
                $"Falta la sección de configuración '{OpenTelemetryOptions.SectionName}' (ServiceName).");

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
                {
                    tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(options.OtlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
                {
                    metrics.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(options.OtlpEndpoint));
                }
            });

        return services;
    }
}
