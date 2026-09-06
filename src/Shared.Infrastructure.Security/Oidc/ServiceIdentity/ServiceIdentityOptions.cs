namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.ServiceIdentity;

/// <summary>
/// Opciones tipadas de la identidad de servicio de este workload (F2-04, OAuth2 Client Credentials —
/// RFC 6749 sección 4.4). Deliberadamente separadas de <see cref="OidcOptions"/>: <see cref="OidcOptions"/>
/// describe la identidad que este proceso valida (tokens de un usuario/BFF que llegan como request
/// entrante), mientras que <see cref="ServiceIdentityOptions"/> describe la identidad propia que este
/// proceso presenta cuando actúa como cliente contra otra API del framework — ambas pueden coexistir en
/// el mismo proyecto (un servicio que valida tokens de usuario y también llama a otro servicio en nombre
/// propio) sin colisionar.
/// </summary>
public sealed class ServiceIdentityOptions
{
    public const string SectionName = "Oidc:ServiceIdentity";

    /// <summary>
    /// URL base del emisor (issuer) del proveedor OIDC/OAuth2 contra el que este workload obtiene su
    /// propio token, por ejemplo "https://keycloak.local/realms/bitcode". Igual que
    /// <see cref="OidcOptions.Authority"/>, se usa para descubrir automáticamente el <c>token_endpoint</c>
    /// -- nunca se hardcodea un endpoint propietario de un proveedor concreto.
    /// </summary>
    public required string Authority { get; set; }

    /// <summary>
    /// Identificador de cliente confidencial de este workload, registrado en el IdP exclusivamente para
    /// Client Credentials (distinto del cliente público usado por Authorization Code + PKCE, F2-02/F2-04
    /// son identidades separadas aunque compartan el mismo IdP).
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Credencial confidencial del cliente (<c>client_secret_post</c>, RFC 6749 sección 2.3.1). Uno de
    /// <see cref="ClientSecret"/> o <see cref="CertificateThumbprint"/> es obligatorio.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Huella del certificado de cliente para autenticación por certificado (mTLS, RFC 8705, o
    /// <c>private_key_jwt</c>, RFC 7523) -- workload identity federada sin secreto compartido. Campo de
    /// contrato preparado a propósito para esa evolución (Plan Maestro sección 2: "OAuth2 Client
    /// Credentials y mTLS cuando existan servicios separados"), pero el flujo concreto de autenticación
    /// por certificado todavía no está implementado en F2-04 -- ver
    /// <c>docs/guia-oidc-adapter.md</c>, sección "F2-04", "queda fuera de alcance". Configurarlo hoy sin
    /// <see cref="ClientSecret"/> falla explícitamente al pedir un token
    /// (<c>ServiceIdentity.CertificateAuthenticationNotSupported</c>), nunca silenciosamente.
    /// </summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>
    /// Scope(s) OAuth2 solicitados al token endpoint (separados por espacio), por ejemplo
    /// "api.productos.read api.productos.write". Opcional -- sin configurar, el IdP aplica el scope por
    /// defecto configurado para este cliente.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Override explícito de la URL del documento de metadata OIDC. Sin configurar, se usa el default
    /// estándar ("{Authority}/.well-known/openid-configuration").
    /// </summary>
    public string? MetadataAddress { get; set; }

    /// <summary>
    /// Exige HTTPS para el endpoint de metadata y el <c>token_endpoint</c> que ese documento referencia.
    /// Verdadero por defecto (Zero Trust, Plan Maestro sección 1).
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}
