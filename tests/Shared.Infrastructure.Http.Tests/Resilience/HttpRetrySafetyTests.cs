using System.Net.Http;
using BitCode.Framework.Shared.Infrastructure.Http.Resilience;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Http.Tests.Resilience;

public class HttpRetrySafetyTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public void IsSafeToRetry_MetodoIdempotentePorEspecificacionHttp_DevuelveTrue(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "https://externo.local/recurso");

        HttpRetrySafety.IsSafeToRetry(request).Should().BeTrue();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    public void IsSafeToRetry_PostOPatchSinMarcadorNiHeaderDeIdempotencia_DevuelveFalse(string method)
    {
        // Criterio de aceptación literal de F1-26: un POST/PATCH sin garantía de idempotencia del
        // lado del receptor nunca se considera seguro de reintentar, aunque el servidor responda un
        // código transitorio (503) — reintentarlo podría duplicar el efecto de negocio.
        var request = new HttpRequestMessage(new HttpMethod(method), "https://externo.local/recurso");

        HttpRetrySafety.IsSafeToRetry(request).Should().BeFalse();
    }

    [Fact]
    public void IsSafeToRetry_PostConHeaderIdempotencyKey_DevuelveTrue()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://externo.local/recurso");
        request.Headers.Add(HttpRetrySafety.IdempotencyKeyHeaderName, "clave-de-prueba");

        HttpRetrySafety.IsSafeToRetry(request).Should().BeTrue();
    }

    [Fact]
    public void IsSafeToRetry_PostMarcadoExplicitamenteSeguro_DevuelveTrue()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://externo.local/recurso");
        request.MarkSafeToRetry();

        HttpRetrySafety.IsSafeToRetry(request).Should().BeTrue();
    }

    [Fact]
    public void IsSafeToRetry_SinRequestDisponibleEnElContexto_DevuelveFalse()
    {
        // Fallar cerrado ante la duda: si la pipeline no puede recuperar el HttpRequestMessage
        // original desde el ResilienceContext, nunca se asume que es seguro reintentar.
        HttpRetrySafety.IsSafeToRetry(null).Should().BeFalse();
    }
}
