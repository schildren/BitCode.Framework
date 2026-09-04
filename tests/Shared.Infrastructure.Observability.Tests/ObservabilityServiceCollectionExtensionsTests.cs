using BitCode.Framework.Shared.Infrastructure.Observability;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace BitCode.Framework.Shared.Infrastructure.Observability.Tests;

public class ObservabilityServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfiguration(string? otlpEndpoint = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:ServiceName"] = "BitCode.Framework.Tests",
                ["OpenTelemetry:OtlpEndpoint"] = otlpEndpoint,
            })
            .Build();

    [Fact]
    public void AddSharedObservability_RegistersTracerAndMeterProviders()
    {
        var services = new ServiceCollection();
        services.AddSharedObservability(BuildConfiguration());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TracerProvider>().Should().NotBeNull();
        provider.GetRequiredService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddSharedObservability_WithoutOpenTelemetrySection_Throws()
    {
        var services = new ServiceCollection();
        var emptyConfiguration = new ConfigurationBuilder().Build();

        var act = () => services.AddSharedObservability(emptyConfiguration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedObservability_WithOtlpEndpointConfigured_DoesNotThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedObservability(BuildConfiguration("http://localhost:4317"));

        act.Should().NotThrow();
    }
}
