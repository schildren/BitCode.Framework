namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc;

/// <summary>
/// Opciones tipadas del adapter OIDC/OAuth2 (F2-01). Deliberadamente agnósticas de proveedor: solo
/// describen los conceptos estándar de OpenID Connect Discovery (RFC/spec
/// "OpenID Connect Discovery 1.0") — Authority (issuer base, usado para construir
/// "{Authority}/.well-known/openid-configuration" salvo que se indique <see cref="MetadataAddress"/>
/// explícito) y Audience (el "aud" esperado en el token, típicamente el identificador del recurso/API
/// protegida). Sustituir el proveedor de identidad (Keycloak, Entra ID, u otro conforme a OIDC) es
/// exclusivamente un cambio de configuración de estos valores — el código que consume estas opciones
/// (<see cref="OidcAuthenticationServiceCollectionExtensions"/>) no debe agregar nunca una propiedad,
/// endpoint o claim propietario de un proveedor concreto (ver ADR 0004).
/// </summary>
public class OidcOptions
{
    public const string SectionName = "Oidc";

    /// <summary>
    /// URL base del emisor (issuer) del proveedor OIDC, por ejemplo
    /// "https://keycloak.local/realms/bitcode". Se usa para descubrir automáticamente el documento de
    /// metadata OIDC estándar y el JWKS de firma — nunca se codifica a mano una clave de firma ni un
    /// endpoint propietario.
    /// </summary>
    public required string Authority { get; set; }

    /// <summary>
    /// Audiencia esperada ("aud") de un token válido para esta API — típicamente el identificador del
    /// recurso/cliente confidencial registrado en el IdP (p. ej. el "client id" del API en Keycloak, o
    /// el "Application ID URI" en Entra ID). No es el ClientId de la aplicación que inicia sesión.
    /// </summary>
    public required string Audience { get; set; }

    /// <summary>
    /// Identificador de cliente OAuth2/OIDC de la aplicación que solicita tokens (Authorization Code +
    /// PKCE, F2-02, o Client Credentials, F2-04). F2-01 solo declara el campo de configuración; el flujo
    /// que lo consume se implementa en esas tareas posteriores.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Override explícito de la URL del documento de metadata OIDC. Sin configurar, se usa el default
    /// estándar del middleware ("{Authority}/.well-known/openid-configuration") — solo debería
    /// completarse si el proveedor expone el documento en una ruta no estándar.
    /// </summary>
    public string? MetadataAddress { get; set; }

    /// <summary>
    /// Exige HTTPS para el endpoint de metadata y los endpoints de token/JWKS que ese documento
    /// referencia. Verdadero por defecto (Zero Trust, Plan Maestro sección 1); solo debería
    /// deshabilitarse en un entorno de desarrollo local contra un IdP sin TLS (p. ej. Keycloak vía
    /// Testcontainers en la propia máquina de CI).
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Tolerancia de reloj (clock skew) aplicada a la validación de vigencia del token ("nbf"/"exp")
    /// para absorber el desfase razonable entre el reloj de este servicio y el del IdP (F2-05). El
    /// framework fija explícitamente 30 segundos como default -- deliberadamente mucho más ajustado
    /// que el default de 5 minutos de <c>TokenValidationParameters</c> en ASP.NET Core, que es
    /// excesivo para Zero Trust (Plan Maestro sección 1): un token cuya vigencia dependa de una
    /// tolerancia de varios minutos amplía innecesariamente la ventana en la que un token robado sigue
    /// siendo válido después de expirar. Solo debería ampliarse si se observan rechazos legítimos por
    /// desfase de reloj entre hosts (deficiencia de NTP), nunca como solución por defecto.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Intervalo de refresco periódico ("background") del documento de metadata OIDC y del JWKS (F2-06,
    /// rotación de claves de firma). Sin configurar, se usa el default del propio
    /// <c>ConfigurationManager&lt;OpenIdConnectConfiguration&gt;</c> de <c>Microsoft.IdentityModel</c>
    /// (24 horas) -- deliberadamente NO es el mecanismo que hace posible "rotación sin downtime": esa
    /// garantía la da <see cref="RefreshOnIssuerKeyNotFound"/> (refresco inmediato ante un <c>kid</c>
    /// desconocido), no este intervalo periódico. Bajar este valor solo tiene sentido si se quiere que el
    /// JWKS se refresque igual aunque nunca aparezca un <c>kid</c> desconocido (p. ej. para detectar
    /// antes una revocación de clave sin esperar a que llegue un token firmado con la clave nueva).
    /// </summary>
    public TimeSpan? JwksAutomaticRefreshInterval { get; set; }

    /// <summary>
    /// Intervalo mínimo entre dos refrescos forzados del JWKS (anti-DoS): acota cada cuánto, como máximo,
    /// un <c>kid</c> desconocido puede disparar una llamada real al IdP vía <see cref="RefreshOnIssuerKeyNotFound"/>.
    /// Sin configurar, se usa el default del <c>ConfigurationManager</c> (5 minutos) -- ese es, en la
    /// práctica, el límite superior de la "ventana de downtime" de una rotación de claves real: un token
    /// firmado con una clave recién rotada puede tardar hasta este intervalo en validarse correctamente
    /// la primera vez tras la rotación (las siguientes veces, el JWKS ya está actualizado). Solo debería
    /// reducirse en pruebas automatizadas que necesiten verificar la rotación sin esperar minutos reales
    /// (el piso técnico impuesto por <c>Microsoft.IdentityModel</c> es 1 segundo -- un valor menor lanza
    /// <see cref="ArgumentOutOfRangeException"/> al arrancar) -- en producción, un valor demasiado bajo
    /// abre una vía de denegación de servicio contra el <c>token_endpoint</c>/JWKS del IdP (un atacante
    /// que envíe repetidamente tokens con un <c>kid</c> inventado podría forzar refrescos constantes).
    /// </summary>
    public TimeSpan? JwksMinimumRefreshInterval { get; set; }

    /// <summary>
    /// Si un token llega firmado con un <c>kid</c> (key id) que no está en el JWKS cacheado, dispara un
    /// refresco inmediato de la metadata OIDC (sujeto a <see cref="JwksMinimumRefreshInterval"/>) y
    /// reintenta la validación con las claves actualizadas antes de rechazar el token -- este es,
    /// concretamente, el mecanismo que hace que la rotación de claves de firma en el IdP (p. ej. Keycloak
    /// generando un nuevo par de claves activo) no requiera reiniciar ni redesplegar este servicio
    /// (criterio de aceptación de F2-06, "Rotación sin downtime"): el middleware estándar de JwtBearer ya
    /// lo hace por defecto (<c>true</c>); esta propiedad lo deja explícito y contractual en vez de
    /// depender de un default implícito del framework subyacente.
    /// </summary>
    public bool RefreshOnIssuerKeyNotFound { get; set; } = true;

    /// <summary>
    /// Rutas de claim JSON de las que <see cref="OidcRoleClaimsTransformation"/> (F2-07) extrae
    /// nombres de rol y los proyecta como <see cref="System.Security.Claims.ClaimTypes.Role"/> en el
    /// <see cref="System.Security.Claims.ClaimsPrincipal"/> autenticado -- necesario porque
    /// <c>JwtBearerHandler</c> no aplana claims anidados: un token de Keycloak (el IdP con el que este
    /// repo integra y prueba, ADR 0004) declara los roles de realm como un objeto anidado
    /// (<c>realm_access: { "roles": [...] }</c>), no como un claim plano de rol. Cada entrada tiene la
    /// forma <c>"claimSuperior.propiedad[.propiedadAnidada...]"</c> (el primer segmento identifica el
    /// claim de nivel superior del token; el resto, la ruta dentro de su valor JSON hasta llegar a un
    /// array de strings) -- resuelta genéricamente, sin ningún nombre de proveedor hardcodeado en el
    /// código de <see cref="OidcRoleClaimsTransformation"/>. El default cubre roles de realm de
    /// Keycloak; agregar <c>"resource_access.&lt;client-id&gt;.roles"</c> extrae también los roles de
    /// un cliente concreto (el nombre del cliente varía por despliegue, por eso no viene en el default).
    /// Una ruta que no resuelva a un array de strings en un token dado (claim ausente, forma distinta,
    /// JSON inválido) se ignora en silencio para esa evaluación -- nunca lanza ni bloquea la
    /// autenticación.
    /// </summary>
    public IList<string> RoleClaimJsonPaths { get; set; } = ["realm_access.roles"];
}
