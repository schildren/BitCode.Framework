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
    /// <param name="additionalMeterNames">
    /// F3-10: nombres de <see cref="System.Diagnostics.Metrics.Meter"/> adicionales a suscribir
    /// (<c>MeterProviderBuilder.AddMeter</c>) — por ejemplo,
    /// <c>KafkaEventingDiagnostics.MeterName</c>/<c>OutboxDiagnostics.MeterName</c>
    /// (<c>Shared.Infrastructure.Messaging.Kafka</c>/<c>Shared.Infrastructure.Persistence</c>). Este
    /// proyecto NO referencia esos paquetes a propósito (un host puede usar observabilidad compartida sin
    /// usar Kafka/Outbox, o viceversa) — el llamador pasa los nombres explícitamente. Sin esto, las
    /// métricas de esos proyectos se siguen acumulando en memoria pero nunca se exportan. Ver
    /// <c>docs/guia-observabilidad-eventos.md</c>.
    /// </param>
    /// <param name="additionalActivitySourceNames">
    /// Igual que <paramref name="additionalMeterNames"/> pero para <see cref="System.Diagnostics.ActivitySource"/>
    /// (<c>TracerProviderBuilder.AddSource</c>) — por ejemplo, <c>KafkaEventingDiagnostics.ActivitySourceName</c>.
    /// </param>
    public static IServiceCollection AddSharedObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<string>? additionalMeterNames = null,
        IEnumerable<string>? additionalActivitySourceNames = null)
    {
        var section = configuration.GetSection(OpenTelemetryOptions.SectionName);
        var options = section.Get<OpenTelemetryOptions>()
            ?? throw new InvalidOperationException(
                $"Falta la sección de configuración '{OpenTelemetryOptions.SectionName}' (ServiceName).");

        var meterNames = additionalMeterNames?.ToArray() ?? Array.Empty<string>();
        var activitySourceNames = additionalActivitySourceNames?.ToArray() ?? Array.Empty<string>();

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                foreach (var sourceName in activitySourceNames)
                {
                    tracing.AddSource(sourceName);
                }

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

                foreach (var meterName in meterNames)
                {
                    metrics.AddMeter(meterName);
                }

                if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
                {
                    metrics.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(options.OtlpEndpoint));
                }
            });

        return services;
    }
}
