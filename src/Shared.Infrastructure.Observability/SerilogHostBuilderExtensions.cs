using Microsoft.Extensions.Hosting;
using Serilog;

namespace BitCode.Framework.Shared.Infrastructure.Observability;

public static class SerilogHostBuilderExtensions
{
    /// <summary>
    /// Configura Serilog leyendo la sección "Serilog" de la configuración (sinks, niveles mínimos,
    /// etc. — ver documentación de Serilog.Settings.Configuration) y enriqueciendo cada log con el
    /// contexto ambiente (LogContext). Llamar antes de Build() sobre el HostBuilder/WebApplicationBuilder.
    /// </summary>
    public static IHostBuilder UseSharedSerilog(this IHostBuilder hostBuilder) =>
        hostBuilder.UseSerilog((context, _, loggerConfiguration) => loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", context.HostingEnvironment.ApplicationName)
            .WriteTo.Console());
}
