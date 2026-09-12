using System.Net.Http.Headers;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.ServiceIdentity;

/// <summary>
/// <see cref="DelegatingHandler"/> que adjunta la identidad propia de este workload
/// (<see cref="IServiceTokenProvider"/>, F2-04) como header <c>Authorization</c> a toda llamada saliente
/// de un <see cref="HttpClient"/> -- el punto de consumo estándar para que un workload llame a otra API
/// del framework en su propio nombre, sin que cada cliente tipado repita la obtención/cacheo del token a
/// mano. Se agrega a un <see cref="IHttpClientBuilder"/> ya registrado con la pipeline de resiliencia
/// estándar del framework (F1-26, <c>AddResilientHttpClient</c>) vía
/// <c>ServiceIdentityServiceCollectionExtensions.AddServiceIdentityAuthentication</c>.
/// </summary>
public sealed class ServiceIdentityAuthenticationHandler : DelegatingHandler
{
    private readonly IServiceTokenProvider _tokenProvider;

    public ServiceIdentityAuthenticationHandler(IServiceTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tokenResult = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (tokenResult.IsFailure)
        {
            // No hay una respuesta HTTP razonable que simular acá (todavía no se envió la request): un
            // workload que no puede autenticarse a sí mismo no debe intentar la llamada sin token --
            // excepción explícita en vez de una request anónima silenciosa hacia una API protegida.
            throw new InvalidOperationException(
                $"No se pudo obtener el token de identidad de servicio ({tokenResult.Error.Code}): {tokenResult.Error.Description}");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(tokenResult.Value.TokenType, tokenResult.Value.AccessToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
