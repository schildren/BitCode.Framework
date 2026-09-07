using System.Collections.Concurrent;
using System.Diagnostics;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Web.MultiTenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BitCode.Framework.Shared.Infrastructure.Web.Tests.MultiTenancy;

/// <summary>
/// F1-15: valida que <see cref="TenantLogEnrichmentMiddleware"/> propaga el <c>TenantId</c> del
/// <see cref="ITenantContext"/> resuelto para el request actual al contexto estructurado de Serilog
/// (<c>LogContext</c>), que la propiedad no se filtra fuera del request, y que dos requests
/// concurrentes con tenants distintos no interfieren entre sí (cada uno ve su propio TenantId).
/// </summary>
public class TenantLogEnrichmentMiddlewareTests
{
    /// <summary>Doble de prueba: <see cref="ITenantContext"/> con un valor fijo conocido de antemano.</summary>
    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public bool IsMultiTenancyEnabled => true;

        public Guid? TenantId => tenantId;
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentBag<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Fact]
    public async Task TenantId_IsPushedToLogContext_DuringRequestProcessing()
    {
        var tenantId = Guid.NewGuid();
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();

        var host = await new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                .ConfigureServices(services => services.AddScoped<ITenantContext>(_ => new FixedTenantContext(tenantId)))
                .Configure(app =>
                {
                    app.UseTenantContextLogging();
                    app.Run(ctx =>
                    {
                        logger.Information("Dentro del request");
                        return ctx.Response.WriteAsync("ok");
                    });
                }))
            .StartAsync();

        using var client = host.GetTestServer().CreateClient();
        await client.GetAsync("/");

        var evt = sink.Events.Should().ContainSingle().Subject;
        evt.Properties.Should().ContainKey("TenantId");
        evt.Properties["TenantId"].ToString().Should().Contain(tenantId.ToString());
    }

    [Fact]
    public async Task LogContext_DoesNotLeak_OutsideRequestProcessing()
    {
        var tenantId = Guid.NewGuid();
        var duringRequestSink = new CapturingSink();
        var loggerDuringRequest = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(duringRequestSink)
            .CreateLogger();

        var host = await new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                .ConfigureServices(services => services.AddScoped<ITenantContext>(_ => new FixedTenantContext(tenantId)))
                .Configure(app =>
                {
                    app.UseTenantContextLogging();
                    app.Run(ctx => ctx.Response.WriteAsync("ok"));
                }))
            .StartAsync();

        using var client = host.GetTestServer().CreateClient();
        await client.GetAsync("/");

        // Fuera de cualquier request: un logger nuevo enriquecido con LogContext no debe ver ninguna
        // propiedad TenantId residual de un request ya finalizado.
        var afterRequestSink = new CapturingSink();
        var loggerAfterRequest = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(afterRequestSink)
            .CreateLogger();
        loggerAfterRequest.Information("Fuera del request");

        var evt = afterRequestSink.Events.Should().ContainSingle().Subject;
        evt.Properties.Should().NotContainKey("TenantId",
            "el TenantId empujado al LogContext durante un request no debe filtrarse a código que corre fuera de ese request");
    }

    /// <summary>
    /// F1-15: dos requests concurrentes, cada uno con su propio TenantId, no deben interferir entre
    /// sí — cada log emitido durante el procesamiento de un request debe llevar únicamente el
    /// TenantId de ESE request, nunca el del otro request en vuelo al mismo tiempo. Esto confirma que
    /// la resolución/propagación funciona naturalmente en contextos async concurrentes (LogContext es
    /// AsyncLocal, y el registro de ITenantContext es Scoped por request).
    /// </summary>
    [Fact]
    public async Task ConcurrentRequests_WithDifferentTenants_DoNotCrossContaminate()
    {
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();

        var host = await new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddHttpContextAccessor();
                    services.AddScoped<ITenantContext>(sp =>
                    {
                        var accessor = sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
                        var tenantId = Guid.Parse(accessor.HttpContext!.Request.Query["tenant"]!);
                        return new FixedTenantContext(tenantId);
                    });
                })
                .Configure(app =>
                {
                    app.UseTenantContextLogging();
                    app.Run(async ctx =>
                    {
                        // Fuerza el intercalado de las dos ejecuciones concurrentes antes de loguear.
                        await Task.Delay(Random.Shared.Next(5, 40));
                        logger.Information("Procesando request");
                        await ctx.Response.WriteAsync("ok");
                    });
                }))
            .StartAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var client = host.GetTestServer().CreateClient();

        await Task.WhenAll(
            client.GetAsync($"/?tenant={tenantA}"),
            client.GetAsync($"/?tenant={tenantB}"));

        sink.Events.Should().HaveCount(2);
        var loggedTenantIds = sink.Events
            .Select(e => e.Properties["TenantId"].ToString().Trim('"'))
            .ToList();

        loggedTenantIds.Should().Contain(tenantA.ToString());
        loggedTenantIds.Should().Contain(tenantB.ToString());
        loggedTenantIds.Should().OnlyHaveUniqueItems(
            "cada request concurrente debe loguear únicamente su propio TenantId, sin mezclarse con el del otro request en vuelo");
    }

    /// <summary>
    /// F4-10: además del enriquecimiento de logs (arriba), el middleware etiqueta el Activity/traza
    /// OTel vigente del request con "tenant_id" -- uno de los atributos de "telemetría mínima" exigidos
    /// por la Fase 4 del Plan Maestro. Sin esto, un collector centralizado (F4-10) recibe trazas
    /// correlacionadas por trace_id/span_id pero sin forma de filtrar/agrupar por tenant.
    /// </summary>
    [Fact]
    public async Task TenantId_IsSetAsActivityTag_DuringRequestProcessing()
    {
        var tenantId = Guid.NewGuid();
        using var activitySource = new ActivitySource(nameof(TenantId_IsSetAsActivityTag_DuringRequestProcessing));
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source == activitySource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        object? capturedTag = null;

        var host = await new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                .ConfigureServices(services => services.AddScoped<ITenantContext>(_ => new FixedTenantContext(tenantId)))
                .Configure(app =>
                {
                    // Simula el Activity que en un host real abre AddAspNetCoreInstrumentation
                    // (Shared.Infrastructure.Observability, F3-10) antes de que corra este middleware.
                    app.Use(async (_, next) =>
                    {
                        using var activity = activitySource.StartActivity("test-request");
                        await next();
                    });
                    app.UseTenantContextLogging();
                    app.Run(ctx =>
                    {
                        capturedTag = Activity.Current?.GetTagItem("tenant_id");
                        return ctx.Response.WriteAsync("ok");
                    });
                }))
            .StartAsync();

        using var client = host.GetTestServer().CreateClient();
        await client.GetAsync("/");

        capturedTag.Should().Be(tenantId);
    }
}
