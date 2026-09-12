using BitCode.Framework.Shared.Infrastructure.Observability;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BitCode.Framework.Shared.Infrastructure.Observability.Tests;

public class SerilogHostBuilderExtensionsTests
{
    [Fact]
    public void UseSharedSerilog_BuildsHostWithoutThrowing()
    {
        var builder = new HostBuilder().UseSharedSerilog();

        var act = () => builder.Build();

        act.Should().NotThrow();
    }

    /// <summary>
    /// F4-10: "Centralizar exportación de logs, métricas y trazas" -- antes de esta tarea,
    /// <see cref="SerilogHostBuilderExtensions.UseSharedSerilog"/> solo escribía a Console, nunca a
    /// OTLP, sin importar la configuración. Con "OpenTelemetry:OtlpEndpoint" configurado (mismo
    /// endpoint que ya usan las trazas/métricas), el host debe seguir construyéndose sin error --
    /// agregar el sink OTLP no debe requerir que el collector esté disponible en tiempo de arranque
    /// (Serilog.Sinks.OpenTelemetry batchea/reintenta en segundo plano, no abre la conexión de forma
    /// síncrona durante Build()).
    /// </summary>
    [Fact]
    public void UseSharedSerilog_WithOtlpEndpointConfigured_BuildsHostWithoutThrowing()
    {
        var builder = new HostBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:ServiceName"] = "BitCode.Framework.Tests",
                ["OpenTelemetry:OtlpEndpoint"] = "http://localhost:4317",
            }))
            .UseSharedSerilog();

        var act = () => builder.Build();

        act.Should().NotThrow();
    }
}
