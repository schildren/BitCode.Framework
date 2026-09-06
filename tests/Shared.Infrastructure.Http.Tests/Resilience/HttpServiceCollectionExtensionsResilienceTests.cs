using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BitCode.Framework.Shared.Infrastructure.Http.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;

namespace BitCode.Framework.Shared.Infrastructure.Http.Tests.Resilience;

/// <summary>
/// Verifica la pipeline de resiliencia real registrada por <see cref="HttpServiceCollectionExtensions.AddResilientHttpClient{TClient}(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{HttpResilienceOptions})"/>
/// (F1-26): sin Docker ni red real, con un <see cref="FakeHttpMessageHandler"/> en el fondo de la
/// pipeline para controlar de forma determinística cuándo "el servicio externo" falla.
/// </summary>
public class HttpServiceCollectionExtensionsResilienceTests
{
    private sealed class TestClient(HttpClient httpClient)
    {
        public HttpClient HttpClient { get; } = httpClient;
    }

    private static (ServiceProvider Provider, FakeHttpMessageHandler Handler) BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        Action<HttpResilienceOptions> configureOptions)
    {
        var handler = new FakeHttpMessageHandler(respond);
        var services = new ServiceCollection();

        services.AddResilientHttpClient<TestClient>(configureOptions)
            .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://externo.local/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return (services.BuildServiceProvider(), handler);
    }

    [Fact]
    public async Task Get_FallaTransitoria503_SeReintentaAutomaticamenteYTerminaConExito()
    {
        var intentos = 0;
        var (provider, handler) = BuildClient(
            _ =>
            {
                intentos++;
                // Falla dos veces (503, transitorio) y recién al tercer intento responde 200.
                return intentos <= 2
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK);
            },
            options =>
            {
                options.RetryMaxAttempts = 3;
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
                options.AttemptTimeout = TimeSpan.FromSeconds(5);
                options.TotalTimeout = TimeSpan.FromSeconds(30);
            });

        using (provider)
        {
            var client = provider.GetRequiredService<TestClient>();

            var response = await client.HttpClient.GetAsync("recurso");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            handler.InvocationCount.Should().Be(3, "el GET es idempotente: la pipeline reintenta automáticamente hasta obtener éxito");
        }
    }

    [Fact]
    public async Task Post_SinMarcadorDeIdempotencia_FallaSinReintentarNiDuplicar()
    {
        var intentos = 0;
        var (provider, handler) = BuildClient(
            _ =>
            {
                intentos++;
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            },
            options =>
            {
                options.RetryMaxAttempts = 3;
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
                options.AttemptTimeout = TimeSpan.FromSeconds(5);
                options.TotalTimeout = TimeSpan.FromSeconds(30);
            });

        using (provider)
        {
            var client = provider.GetRequiredService<TestClient>();

            var response = await client.HttpClient.PostAsync("recurso", new StringContent("{}"));

            // Criterio de aceptación literal de F1-26: un POST sin garantía de idempotencia del lado
            // del receptor NUNCA se reintenta automáticamente, aunque el servicio responda un código
            // transitorio (503) — el riesgo de duplicar el efecto de negocio pesa más que la
            // posibilidad de que un reintento hubiera tenido éxito.
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            handler.InvocationCount.Should().Be(1, "un POST sin Idempotency-Key ni marca explícita nunca se reintenta");
        }
    }

    [Fact]
    public async Task Post_ConHeaderIdempotencyKey_SeReintentaComoUnaOperacionSegura()
    {
        var intentos = 0;
        var (provider, handler) = BuildClient(
            _ =>
            {
                intentos++;
                return intentos <= 1
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK);
            },
            options =>
            {
                options.RetryMaxAttempts = 3;
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
                options.AttemptTimeout = TimeSpan.FromSeconds(5);
                options.TotalTimeout = TimeSpan.FromSeconds(30);
            });

        using (provider)
        {
            var client = provider.GetRequiredService<TestClient>();
            using var request = new HttpRequestMessage(HttpMethod.Post, "recurso") { Content = new StringContent("{}") };
            request.Headers.Add(HttpRetrySafety.IdempotencyKeyHeaderName, "clave-de-prueba");

            var response = await client.HttpClient.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            handler.InvocationCount.Should().Be(2, "un POST con Idempotency-Key ya se declara deduplicable del lado del receptor, así que es seguro reintentarlo");
        }
    }

    [Fact]
    public async Task CircuitBreaker_AbreTrasFallosConsecutivos_FallaRapidoSinInvocarElHandler()
    {
        var (provider, handler) = BuildClient(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            options =>
            {
                // POST (inseguro de reintentar) para que cada llamada del test sea exactamente un
                // intento real contra el handler, sin que el propio retry complique el conteo — el
                // valor de MaxRetryAttempts es irrelevante en este escenario porque el predicado de
                // HttpRetrySafety ya descarta el reintento para un POST sin marcador de idempotencia.
                options.RetryMaxAttempts = 1;
                options.AttemptTimeout = TimeSpan.FromSeconds(5);
                options.TotalTimeout = TimeSpan.FromSeconds(30);
                options.CircuitBreakerMinimumThroughput = 2;
                options.CircuitBreakerFailureRatio = 0.5;
                options.CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(10);
                options.CircuitBreakerBreakDuration = TimeSpan.FromSeconds(5);
            });

        using (provider)
        {
            var client = provider.GetRequiredService<TestClient>();

            // Dos fallos consecutivos alcanzan el mínimo de llamadas (2) con 100% de fallos (>= 50%
            // configurado): el circuito debe abrirse.
            (await client.HttpClient.PostAsync("recurso", new StringContent("{}"))).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await client.HttpClient.PostAsync("recurso", new StringContent("{}"))).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            handler.InvocationCount.Should().Be(2);

            // Con el circuito abierto, la tercera llamada debe fallar de inmediato con
            // BrokenCircuitException, SIN volver a invocar el handler (falla rápido en vez de colgar
            // el timeout completo en cada intento, como pide el alcance de F1-26).
            var accion = async () => await client.HttpClient.PostAsync("recurso", new StringContent("{}"));

            await accion.Should().ThrowAsync<BrokenCircuitException>();
            handler.InvocationCount.Should().Be(2, "con el circuito abierto, la pipeline no debe volver a invocar el servicio externo");
        }
    }
}
