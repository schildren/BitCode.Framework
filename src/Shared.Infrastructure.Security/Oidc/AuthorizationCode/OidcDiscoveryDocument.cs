namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Subconjunto del documento de metadata OIDC estándar ("OpenID Connect Discovery 1.0") relevante para
/// el flujo Authorization Code + PKCE: los endpoints de autorización y de token. Igual que
/// <see cref="OidcOptions"/> (F2-01), se resuelve exclusivamente por descubrimiento estándar -- nunca se
/// hardcodea un endpoint propietario de un proveedor concreto.
/// </summary>
/// <param name="EndSessionEndpoint">
/// Endpoint estándar de RP-Initiated Logout ("OpenID Connect RP-Initiated Logout 1.0"), opcional --
/// no todos los proveedores lo publican. F2-03 (BFF) lo usa, si está presente, para invalidar también
/// la sesión SSO del IdP al cerrar sesión, no solo la sesión propia del BFF.
/// </param>
public sealed record OidcDiscoveryDocument(string AuthorizationEndpoint, string TokenEndpoint, string? EndSessionEndpoint = null);

/// <summary>
/// Resuelve (y cachea) el <see cref="OidcDiscoveryDocument"/> de un proveedor OIDC a partir de
/// <see cref="OidcOptions.Authority"/>/<see cref="OidcOptions.MetadataAddress"/>.
/// </summary>
public interface IOidcDiscoveryDocumentProvider
{
    Task<OidcDiscoveryDocument> GetAsync(CancellationToken cancellationToken = default);
}
