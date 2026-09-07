using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace BitCode.Framework.Shared.Infrastructure.Observability;

public static class SerilogHostBuilderExtensions
{
    /// <summary>
    /// Configura Serilog leyendo la sección "Serilog" de la configuración (sinks, niveles mínimos,
    /// etc. — ver documentación de Serilog.Settings.Configuration) y enriqueciendo cada log con el
    /// contexto ambiente (LogContext). Llamar antes de Build() sobre el HostBuilder/WebApplicationBuilder.
    /// </summary>
    /// <remarks>
    /// F4-10: además de <c>WriteTo.Console()</c> (siempre activo), agrega <c>WriteTo.OpenTelemetry(...)</c>
    /// SOLO si la sección "OpenTelemetry" (misma que consume <see cref="ObservabilityServiceCollectionExtensions.AddSharedObservability"/>,
    /// F3-10) tiene <c>OtlpEndpoint</c> configurado — sin esto, un host sin collector (desarrollo local)
    /// no intenta abrir ninguna conexión OTLP nueva; Console sigue siendo el único sink, igual que antes
    /// de esta tarea. Con <c>OtlpEndpoint</c> configurado, los tres pilares de la telemetría (logs,
    /// métricas, trazas) via OTLP quedan centralizados en el mismo collector — cierra la brecha que el
    /// nombre de la fila del backlog señala explícitamente ("Centralizar exportación de LOGS, métricas y
    /// trazas"), que hasta esta tarea solo cubría métricas/trazas.
    /// </remarks>
    public static IHostBuilder UseSharedSerilog(this IHostBuilder hostBuilder) =>
        hostBuilder.UseSerilog((context, _, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", context.HostingEnvironment.ApplicationName)
                .WriteTo.Console();

            var otelSection = context.Configuration.GetSection(OpenTelemetryOptions.SectionName);
            var otlpEndpoint = otelSection["OtlpEndpoint"];
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                var serviceName = otelSection["ServiceName"] ?? context.HostingEnvironment.ApplicationName;
                loggerConfiguration.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpEndpoint;
                    options.Protocol = OtlpProtocol.Grpc;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = serviceName,
                    };
                });
            }
        });
}
