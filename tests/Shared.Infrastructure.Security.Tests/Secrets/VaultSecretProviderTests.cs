using System.Net;
using System.Text;
using BitCode.Framework.Shared.Infrastructure.Security.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Secrets;

/// <summary>
/// F2-12: pruebas unitarias de <see cref="VaultSecretProvider"/> contra un
/// <see cref="HttpMessageHandler"/> falso (sin un Vault real -- eso lo cubre
/// <c>Integration/VaultSecretProviderIntegrationTests.cs</c> vía Testcontainers). Cubre el mapeo de
/// las respuestas HTTP estándar de la API KV v2 de Vault a <c>Result&lt;string&gt;</c>, nunca a una
/// excepción sin traducir.
/// </summary>
public class VaultSecretProviderTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }

    private static VaultSecretProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        out FakeHttpMessageHandler handler,
        string mountPath = "secret",
        string pathPrefix = "bitcode")
    {
        handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://vault.local:8200/") };
        var options = Options.Create(new VaultSecretProviderOptions
        {
            Address = "http://vault.local:8200",
            Token = "root-token",
            MountPath = mountPath,
            PathPrefix = pathPrefix,
        });
        return new VaultSecretProvider(httpClient, options);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task GetSecretAsync_WithSuccessfulResponse_ReturnsValueFromDataDataValueField()
    {
        var provider = CreateProvider(_ => JsonResponse(HttpStatusCode.OK,
            """{"data":{"data":{"value":"s3cr3t-value"},"metadata":{"version":1}}}"""), out var handler);

        var result = await provider.GetSecretAsync("mi-clave");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("s3cr3t-value");
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v1/secret/data/bitcode/mi-clave");
        handler.LastRequest.Headers.GetValues("X-Vault-Token").Should().ContainSingle().Which.Should().Be("root-token");
    }

    [Fact]
    public async Task GetSecretAsync_With404_ReturnsFailureNotFound()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.NotFound), out _);

        var result = await provider.GetSecretAsync("no-existe");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Secrets.NotFound");
    }

    [Fact]
    public async Task GetSecretAsync_With403_ReturnsFailureAccessDenied()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), out _);

        var result = await provider.GetSecretAsync("mi-clave");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Secrets.AccessDenied");
    }

    [Fact]
    public async Task GetSecretAsync_With401_ReturnsFailureAccessDenied()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized), out _);

        var result = await provider.GetSecretAsync("mi-clave");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Secrets.AccessDenied");
    }

    [Fact]
    public async Task GetSecretAsync_With500_ReturnsFailureProviderUnavailable()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("vault sealed"),
        }, out _);

        var result = await provider.GetSecretAsync("mi-clave");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Secrets.ProviderUnavailable");
    }

    [Fact]
    public async Task GetSecretAsync_WithSuccessfulResponseMissingValueField_ReturnsFailureNotFound()
    {
        var provider = CreateProvider(_ => JsonResponse(HttpStatusCode.OK,
            """{"data":{"data":{"otro-campo":"x"}}}"""), out _);

        var result = await provider.GetSecretAsync("mi-clave");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Secrets.NotFound");
    }

    [Fact]
    public async Task GetSecretAsync_WithEmptyKey_ReturnsFailureValidation_WithoutCallingVault()
    {
        var provider = CreateProvider(_ => throw new InvalidOperationException("No debería llamar a Vault."), out _);

        var result = await provider.GetSecretAsync(string.Empty);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Secrets.InvalidKey");
    }

    [Fact]
    public async Task GetSecretAsync_WithoutPathPrefix_BuildsPathWithoutExtraSegment()
    {
        var provider = CreateProvider(_ => JsonResponse(HttpStatusCode.OK,
            """{"data":{"data":{"value":"x"}}}"""), out var handler, pathPrefix: string.Empty);

        await provider.GetSecretAsync("mi-clave");

        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v1/secret/data/mi-clave");
    }
}
