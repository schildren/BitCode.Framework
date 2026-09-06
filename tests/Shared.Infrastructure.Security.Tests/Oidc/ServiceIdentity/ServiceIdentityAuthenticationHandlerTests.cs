using BitCode.Framework.Shared.Infrastructure.Security.Oidc.ServiceIdentity;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc.ServiceIdentity;

/// <summary>
/// F2-04: <see cref="ServiceIdentityAuthenticationHandler"/> es el punto de consumo estándar que
/// adjunta la identidad de servicio a una llamada saliente -- verifica que el header
/// <c>Authorization</c> se arma con el token/tipo obtenidos de <see cref="IServiceTokenProvider"/>, y
/// que un fallo al obtener el token nunca deja pasar la request en forma anónima.
/// </summary>
public class ServiceIdentityAuthenticationHandlerTests
{
    private sealed class PassthroughHandler : DelegatingHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task SendAsync_WithValidToken_AttachesAuthorizationHeader()
    {
        var tokenProvider = Substitute.For<IServiceTokenProvider>();
        tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ServiceAccessToken("svc-at-123", "Bearer", DateTimeOffset.UtcNow.AddMinutes(5))));

        var inner = new PassthroughHandler();
        var handler = new ServiceIdentityAuthenticationHandler(tokenProvider) { InnerHandler = inner };
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bitcode.local/productos");
        await invoker.SendAsync(request, CancellationToken.None);

        inner.LastRequest!.Headers.Authorization.Should().NotBeNull();
        inner.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        inner.LastRequest.Headers.Authorization.Parameter.Should().Be("svc-at-123");
    }

    [Fact]
    public async Task SendAsync_WhenTokenProviderFails_ThrowsWithoutForwardingRequest()
    {
        var tokenProvider = Substitute.For<IServiceTokenProvider>();
        tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ServiceAccessToken>(Error.Unauthorized("ServiceIdentity.TokenRequest.invalid_client", "Client secret inválido")));

        var inner = new PassthroughHandler();
        var handler = new ServiceIdentityAuthenticationHandler(tokenProvider) { InnerHandler = inner };
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bitcode.local/productos");
        var act = async () => await invoker.SendAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ServiceIdentity.TokenRequest.invalid_client*");
        inner.LastRequest.Should().BeNull("la request nunca debe reenviarse en forma anónima si no se pudo obtener el token de servicio");
    }
}
