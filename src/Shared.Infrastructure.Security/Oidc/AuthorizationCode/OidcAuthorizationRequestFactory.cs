using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Implementación estándar de <see cref="IOidcAuthorizationRequestFactory"/>: resuelve el
/// <c>authorization_endpoint</c> por descubrimiento OIDC (<see cref="IOidcDiscoveryDocumentProvider"/>,
/// nunca hardcodeado -- ADR 0004) y arma la URL con PKCE + state + nonce.
/// </summary>
public sealed class OidcAuthorizationRequestFactory : IOidcAuthorizationRequestFactory
{
    private readonly IOidcDiscoveryDocumentProvider _discoveryDocumentProvider;
    private readonly IOptions<OidcOptions> _oidcOptions;
    private readonly IOptions<OidcAuthorizationCodeFlowOptions> _flowOptions;

    public OidcAuthorizationRequestFactory(
        IOidcDiscoveryDocumentProvider discoveryDocumentProvider,
        IOptions<OidcOptions> oidcOptions,
        IOptions<OidcAuthorizationCodeFlowOptions> flowOptions)
    {
        _discoveryDocumentProvider = discoveryDocumentProvider;
        _oidcOptions = oidcOptions;
        _flowOptions = flowOptions;
    }

    public async Task<OidcAuthorizationRequest> CreateAsync(string? returnUrl, CancellationToken cancellationToken = default)
    {
        var oidc = _oidcOptions.Value;
        var flow = _flowOptions.Value;

        if (string.IsNullOrWhiteSpace(oidc.ClientId))
        {
            throw new InvalidOperationException(
                $"'{OidcOptions.SectionName}:{nameof(OidcOptions.ClientId)}' es obligatorio para Authorization Code + PKCE (F2-02).");
        }

        var discoveryDocument = await _discoveryDocumentProvider.GetAsync(cancellationToken);

        var codeVerifier = PkceGenerator.GenerateCodeVerifier();
        var codeChallenge = PkceGenerator.CreateCodeChallenge(codeVerifier);
        var state = PkceGenerator.GenerateCorrelationToken();
        var nonce = PkceGenerator.GenerateCorrelationToken();

        // response_type=code es el ÚNICO valor que este framework construye -- nunca "token" ni
        // "id_token" (el flujo implícito quedaría prohibido por el criterio de aceptación de F2-02
        // incluso si un consumidor intentara pasarlo, porque no hay ningún parámetro que lo permita).
        var query = string.Join(
            '&',
            "response_type=code",
            $"client_id={Uri.EscapeDataString(oidc.ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(flow.RedirectUri)}",
            $"scope={Uri.EscapeDataString(flow.Scope)}",
            $"state={Uri.EscapeDataString(state)}",
            $"nonce={Uri.EscapeDataString(nonce)}",
            $"code_challenge={Uri.EscapeDataString(codeChallenge)}",
            "code_challenge_method=S256");

        var separator = discoveryDocument.AuthorizationEndpoint.Contains('?') ? '&' : '?';
        var authorizationUri = new Uri($"{discoveryDocument.AuthorizationEndpoint}{separator}{query}");

        var flowState = new OidcAuthorizationCodeState(state, codeVerifier, nonce, flow.RedirectUri, returnUrl);

        return new OidcAuthorizationRequest(authorizationUri, flowState);
    }
}
