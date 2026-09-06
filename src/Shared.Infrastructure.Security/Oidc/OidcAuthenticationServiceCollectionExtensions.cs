using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc;

/// <summary>
/// Adapter OIDC/OAuth2 intercambiable por configuración (F2-01, ADR 0004). Registra autenticación
/// JWT Bearer configurada exclusivamente contra el estándar OpenID Connect Discovery: el middleware
/// obtiene issuer, endpoints y claves de firma (JWKS) automáticamente desde
/// "{Authority}/.well-known/openid-configuration" en vez de una clave simétrica embebida — el mismo
/// código funciona sin cambios contra Keycloak, Microsoft Entra ID o cualquier otro proveedor conforme
/// a OIDC: apuntar a otro proveedor es exclusivamente cambiar la sección de configuración "Oidc"
/// (Authority/Audience), nunca una recompilación.
/// </summary>
public static class OidcAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Registra autenticación JWT Bearer validada contra un proveedor OIDC/OAuth2 externo, leyendo
    /// <see cref="OidcOptions"/> de la sección de configuración "Oidc" (Authority/Audience obligatorios).
    /// </summary>
    /// <remarks>
    /// Alcance de F2-01: deja el adapter de autenticación (issuer/audience/firma resueltos por
    /// descubrimiento OIDC estándar) listo para que F2-05 agregue la política explícita de validación
    /// (vigencia, clock skew afinado, casos negativos automatizados) y para que F2-02/F2-03 construyan
    /// sobre él el flujo Authorization Code + PKCE y el BFF. No reemplaza a
    /// <see cref="SecurityServiceCollectionExtensions.AddSharedSecurity{TUser,TRole,TContext}"/> (JWT
    /// propio, emisión local con clave simétrica) dentro de la misma tarea: ambos registran el mismo
    /// esquema de autenticación ("Bearer") y son mutuamente excluyentes en un mismo proyecto — un
    /// proyecto nuevo o migrado a OIDC llama a este método en lugar de <c>AddSharedSecurity</c> para la
    /// parte de autenticación (la emisión/gestión de Identity, roles y permisos de
    /// <c>AddSharedSecurity</c> sigue siendo válida de forma independiente hasta que una tarea posterior
    /// evalúe cómo evaluar permisos también contra claims de un IdP externo). El JWT propio no se marca
    /// obsoleto ni se retira en esta tarea (ver ADR 0004 y <c>docs/guia-oidc-adapter.md</c>) para no
    /// romper a los consumidores existentes (<c>samples/Sample.Api</c> y proyectos ya generados con
    /// <c>AddSharedSecurity</c>) sin el análisis y período de gracia que exige
    /// <c>docs/politica-versionado.md</c> (sección 3).
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// La sección de configuración "Oidc" no existe o le faltan Authority/Audience.
    /// </exception>
    public static IServiceCollection AddSharedOidcAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<JwtBearerOptions>? configureJwtBearer = null)
    {
        var oidcSection = configuration.GetSection(OidcOptions.SectionName);
        services.Configure<OidcOptions>(oidcSection);
        var oidcOptions = oidcSection.Get<OidcOptions>()
            ?? throw new InvalidOperationException(
                $"Falta la sección de configuración '{OidcOptions.SectionName}' (Authority/Audience).");

        if (string.IsNullOrWhiteSpace(oidcOptions.Authority))
        {
            throw new InvalidOperationException(
                $"'{OidcOptions.SectionName}:{nameof(OidcOptions.Authority)}' es obligatorio para el adapter OIDC/OAuth2 (F2-01).");
        }

        if (string.IsNullOrWhiteSpace(oidcOptions.Audience))
        {
            throw new InvalidOperationException(
                $"'{OidcOptions.SectionName}:{nameof(OidcOptions.Audience)}' es obligatorio para el adapter OIDC/OAuth2 (F2-01).");
        }

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                // Authority es el único dato que determina de dónde vienen issuer/claves de firma: el
                // middleware descubre "{Authority}/.well-known/openid-configuration" (o
                // MetadataAddress si se lo indica explícitamente) y refresca el JWKS automáticamente —
                // no hay clave simétrica ni endpoint propietario embebido en el código (ADR 0004).
                options.Authority = oidcOptions.Authority;
                options.Audience = oidcOptions.Audience;
                options.RequireHttpsMetadata = oidcOptions.RequireHttpsMetadata;

                if (!string.IsNullOrWhiteSpace(oidcOptions.MetadataAddress))
                {
                    options.MetadataAddress = oidcOptions.MetadataAddress;
                }

                // F2-06, "Rotación sin downtime": ante un token firmado con un "kid" que el JWKS
                // cacheado no conoce (rotación de claves en el IdP), el middleware refresca la metadata
                // OIDC (issuer + JWKS) y reintenta antes de rechazar el token, en vez de exigir un
                // reinicio o redeploy para reconocer la clave nueva. Explícito y contractual (ver
                // OidcOptions.RefreshOnIssuerKeyNotFound) en vez de depender de que el default del
                // middleware subyacente (también true) no cambie en una versión futura.
                options.RefreshOnIssuerKeyNotFound = oidcOptions.RefreshOnIssuerKeyNotFound;

                // Solo se reemplaza el ConfigurationManager por defecto del middleware si la
                // configuración pide explícitamente afinar sus intervalos de refresco (F2-06) -- en el
                // caso general (ninguno de los dos configurado) el middleware sigue resolviendo su
                // propio ConfigurationManager con los defaults estándar de Microsoft.IdentityModel.
                if (oidcOptions.JwksAutomaticRefreshInterval is not null || oidcOptions.JwksMinimumRefreshInterval is not null)
                {
                    var metadataAddress = !string.IsNullOrWhiteSpace(oidcOptions.MetadataAddress)
                        ? oidcOptions.MetadataAddress
                        : $"{oidcOptions.Authority.TrimEnd('/')}/.well-known/openid-configuration";

                    var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        metadataAddress,
                        new OpenIdConnectConfigurationRetriever(),
                        new HttpDocumentRetriever { RequireHttps = oidcOptions.RequireHttpsMetadata });

                    if (oidcOptions.JwksAutomaticRefreshInterval is { } automaticRefreshInterval)
                    {
                        configurationManager.AutomaticRefreshInterval = automaticRefreshInterval;
                    }

                    if (oidcOptions.JwksMinimumRefreshInterval is { } minimumRefreshInterval)
                    {
                        configurationManager.RefreshInterval = minimumRefreshInterval;
                    }

                    options.ConfigurationManager = configurationManager;
                }

                // Política de validación explícita y contractual (F2-05, no un default implícito del
                // middleware): issuer, audience, firma y vigencia se validan siempre contra lo resuelto
                // por OIDC Discovery a partir de Authority -- nunca se acepta un token sin firma
                // verificable ("alg: none"), de un issuer distinto al configurado, para una audience
                // distinta a la de este recurso, ni ya expirado. La tolerancia de reloj (ClockSkew) es
                // explícita y configurable (ver OidcOptions.ClockSkew) en vez de depender del default de
                // 5 minutos de TokenValidationParameters.
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = oidcOptions.ClockSkew,
                };

                configureJwtBearer?.Invoke(options);
            });

        return services;
    }
}
