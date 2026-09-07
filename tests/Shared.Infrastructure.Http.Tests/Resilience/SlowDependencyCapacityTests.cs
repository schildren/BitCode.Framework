using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BitCode.Framework.Shared.Infrastructure.Http.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Http.Tests.Resilience;

/// <summary>
/// F4-14 (Fase 4 — Capacity tests): "Dependencia lenta" es una de las 10 pruebas obligatorias
/// listadas en la sección "Pruebas obligatorias" de la Fase 4 del Plan Maestro. Ejercita la pipeline
/// de resiliencia HTTP REAL construida en F1-26 (<c>AddResilientHttpClient</c>, ver
/// <c>docs/guia-resiliencia-http.md</c>) contra un handler que retrasa deliberadamente su respuesta
/// más allá de <c>AttemptTimeout</c> — sin red real ni Docker (mismo patrón que
/// <see cref="HttpServiceCollectionExtensionsResilienceTests"/>, con un handler que agrega latencia
/// real en vez de fallar con un código HTTP), confirma que el llamador nunca queda colgado
/// indefinidamente esperando una dependencia lenta.
/// </summary>
public class SlowDependencyCapacityTests
{
    private sealed class TestClient(HttpClient httpClient)
    {
        public HttpClient HttpClient { get; } = httpClient;
    }

    /// <summary>Handler que tarda <paramref name="delay"/> antes de responder — simula una dependencia lenta real (no un fallo HTTP).</summary>
    private sealed class SlowHttpMessageHandler(TimeSpan delay, Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            await Task.Delay(delay, cancellationToken);
            return respond(request);
        }
    }

    [Fact]
    public async Task Get_DependenciaSiempreLenta_FallaPorTimeoutDeIntentoEnVezDeColgarIndefinidamente()
    {
        // El handler SIEMPRE tarda 2s más que AttemptTimeout (500ms) -- ninguna respuesta llega a
        // tiempo en ningún intento. TotalTimeout (1.5s) acota además el presupuesto agregado de los
        // reintentos, para que el test tenga un techo real de cuánto puede tardar en total.
        var handler = new SlowHttpMessageHandler(
            TimeSpan.FromSeconds(2.5),
            _ => new HttpResponseMessage(HttpStatusCode.OK));

        var services = new ServiceCollection();
        services.AddResilientHttpClient<TestClient>(options =>
            {
                options.RetryMaxAttempts = 2;
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
                options.AttemptTimeout = TimeSpan.FromMilliseconds(500);
                options.TotalTimeout = TimeSpan.FromSeconds(1.5);
            })
            .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://externo-lento.local/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<TestClient>();

        var stopwatch = Stopwatch.StartNew();
        var accion = async () => await client.HttpClient.GetAsync("recurso");
        // Polly.Timeout.TimeoutRejectedException (TotalTimeout) envolviendo un TaskCanceledException
        // interno (AttemptTimeout) -- confirma que AMBOS timeouts de la pipeline (F1-26) actúan
        // realmente, no solo uno de los dos.
        await accion.Should().ThrowAsync<Polly.Timeout.TimeoutRejectedException>(
            "TotalTimeout (1.5s) debe cortar el presupuesto agregado de reintentos mucho antes de que " +
            "el handler lento (2.5s) llegue a responder en cualquier intento individual");
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2.5),
            "el llamador NUNCA debe esperar el tiempo completo de la dependencia lenta (2.5s) -- " +
            "TotalTimeout debe cortar la espera bastante antes de eso, exactamente el problema #1 " +
            "que docs/guia-resiliencia-http.md documenta que esta pipeline resuelve");
    }

    [Fact]
    public async Task Get_DependenciaLentaUnaVez_SeRecuperaEnElReintentoDentroDelPresupuesto()
    {
        // Primera invocación lenta (dispara AttemptTimeout); segunda invocación responde rápido y
        // exitosamente -- confirma que un GET (idempotente) se reintenta automáticamente tras un
        // timeout de intento, sin que el llamador tenga que implementar su propio retry a mano.
        var invocationCount = 0;
        var handler = new SlowHttpMessageHandler(TimeSpan.Zero, _ => new HttpResponseMessage(HttpStatusCode.OK));

        var services = new ServiceCollection();
        services.AddResilientHttpClient<TestClient>(options =>
            {
                options.RetryMaxAttempts = 3;
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
                options.AttemptTimeout = TimeSpan.FromMilliseconds(500);
                options.TotalTimeout = TimeSpan.FromSeconds(5);
            })
            .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://externo-lento.local/"))
            .ConfigurePrimaryHttpMessageHandler(() => new ConditionallySlowHandler(() =>
            {
                invocationCount++;
                return invocationCount == 1 ? TimeSpan.FromSeconds(2) : TimeSpan.Zero;
            }));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<TestClient>();

        var response = await client.HttpClient.GetAsync("recurso");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        invocationCount.Should().Be(2,
            "el primer intento debe agotar AttemptTimeout (dependencia lenta) y el segundo, ya rápido, debe completar con éxito");
    }

    private sealed class ConditionallySlowHandler(Func<TimeSpan> nextDelay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var delay = nextDelay();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
