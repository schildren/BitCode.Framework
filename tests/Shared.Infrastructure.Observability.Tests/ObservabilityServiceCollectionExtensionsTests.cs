using System.Reflection;
using BitCode.Framework.Shared.Infrastructure.Observability;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
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

    /// <summary>
    /// F4-10: "service.instance.id"/"deployment.environment" son dos de los atributos de "telemetría
    /// mínima" exigidos por la Fase 4 del Plan Maestro (instance/module) que hasta esta tarea no se
    /// seteaban en el Resource -- sin ellos, un OTel Collector centralizado (F4-10) recibe trazas de
    /// múltiples réplicas sin forma de distinguir de cuál vino cada una.
    /// </summary>
    [Fact]
    public void AddSharedObservability_SetsInstanceAndEnvironmentResourceAttributes()
    {
        Environment.SetEnvironmentVariable("HOSTNAME", "pod-sample-api-abc123");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Staging");
        try
        {
            var services = new ServiceCollection();
            services.AddSharedObservability(BuildConfiguration());

            using var provider = services.BuildServiceProvider();
            var tracerProvider = provider.GetRequiredService<TracerProvider>();

            // No hay un accessor público de "Resource" en TracerProvider en esta versión del SDK
            // (OpenTelemetry 1.18.0) -- se lee vía reflexión la propiedad interna "Resource" de la
            // implementación concreta (TracerProviderSdk), únicamente para verificar en la prueba lo
            // que ObserveResourceServiceCollectionExtensions ya configuró en producción; el código de
            // producción no depende de reflexión en ningún punto.
            var resourceProperty = tracerProvider.GetType()
                .GetProperty("Resource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new InvalidOperationException(
                    "No se encontró la propiedad \"Resource\" en la implementación de TracerProvider -- " +
                    "revisar si cambió el SDK de OpenTelemetry.");
            var resource = (Resource)resourceProperty.GetValue(tracerProvider)!;

            resource.Attributes.Should().Contain(
                new KeyValuePair<string, object>("service.instance.id", "pod-sample-api-abc123"));
            resource.Attributes.Should().Contain(
                new KeyValuePair<string, object>("deployment.environment", "Staging"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOSTNAME", null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }
    }
}
