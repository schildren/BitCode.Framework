using BitCode.Framework.Shared.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Registra las piezas del flujo Authorization Code + PKCE (F2-02) sobre el adapter OIDC de F2-01: el
/// resolutor de metadata (<see cref="IOidcDiscoveryDocumentProvider"/>), el constructor de la request de
/// autorización (<see cref="IOidcAuthorizationRequestFactory"/>), el intercambiador de code por tokens
/// (<see cref="IOidcAuthorizationCodeExchanger"/>) y el protector de la cookie de correlación
/// (<see cref="IOidcAuthorizationCodeStateProtector"/>). No mapea ningún endpoint HTTP -- eso lo hace
/// <c>MapSharedOidcAuthorizationCodeLogin</c> en Shared.Infrastructure.Web, que depende de este proyecto
/// exclusivamente por estas abstracciones, nunca al revés.
/// </summary>
public static class OidcAuthorizationCodeServiceCollectionExtensions
{
    /// <summary>
    /// Lee las secciones de configuración "Oidc" (Authority/ClientId, ya validadas por
    /// <see cref="OidcAuthenticationServiceCollectionExtensions.AddSharedOidcAuthentication"/> si el
    /// proyecto también valida tokens, aunque no es un prerrequisito llamar a ese método primero) y
    /// "Oidc:AuthorizationCode" (RedirectUri obligatorio) y registra los servicios del flujo.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Falta "Oidc:Authority", "Oidc:ClientId" o "Oidc:AuthorizationCode:RedirectUri".
    /// </exception>
    public static IServiceCollection AddSharedOidcAuthorizationCodeFlow(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var oidcSection = configuration.GetSection(OidcOptions.SectionName);
        services.Configure<OidcOptions>(oidcSection);
        var oidcOptions = oidcSection.Get<OidcOptions>()
            ?? throw new InvalidOperationException(
                $"Falta la sección de configuración '{OidcOptions.SectionName}' (Authority/ClientId).");

        if (string.IsNullOrWhiteSpace(oidcOptions.Authority))
        {
            throw new InvalidOperationException(
                $"'{OidcOptions.SectionName}:{nameof(OidcOptions.Authority)}' es obligatorio para Authorization Code + PKCE (F2-02).");
        }

        if (string.IsNullOrWhiteSpace(oidcOptions.ClientId))
        {
            throw new InvalidOperationException(
                $"'{OidcOptions.SectionName}:{nameof(OidcOptions.ClientId)}' es obligatorio para Authorization Code + PKCE (F2-02).");
        }

        var flowSection = configuration.GetSection(OidcAuthorizationCodeFlowOptions.SectionName);
        services.Configure<OidcAuthorizationCodeFlowOptions>(flowSection);
        var flowOptions = flowSection.Get<OidcAuthorizationCodeFlowOptions>()
            ?? throw new InvalidOperationException(
                $"Falta la sección de configuración '{OidcAuthorizationCodeFlowOptions.SectionName}' (RedirectUri).");

        if (string.IsNullOrWhiteSpace(flowOptions.RedirectUri))
        {
            throw new InvalidOperationException(
                $"'{OidcAuthorizationCodeFlowOptions.SectionName}:{nameof(OidcAuthorizationCodeFlowOptions.RedirectUri)}' es obligatorio para Authorization Code + PKCE (F2-02).");
        }

        // Data Protection: cifra/firma la cookie de correlación (state/code_verifier/nonce/returnUrl).
        // AddDataProtection() usa TryAdd internamente -- segura de llamar aunque el proyecto consumidor
        // ya la haya registrado por otro motivo.
        services.AddDataProtection();
        services.AddSingleton<IOidcAuthorizationCodeStateProtector, OidcAuthorizationCodeStateProtector>();
        services.AddSingleton<OidcDiscoveryDocumentCache>();

        // F1-26: mismos timeout/retry/circuit-breaker que cualquier otra llamada HTTP saliente del
        // framework -- ni la resolución de metadata ni el intercambio de code reimplementan resiliencia.
        services.AddResilientHttpClient<OidcDiscoveryDocumentProvider>();
        services.AddTransient<IOidcDiscoveryDocumentProvider>(sp => sp.GetRequiredService<OidcDiscoveryDocumentProvider>());

        services.AddResilientHttpClient<OidcAuthorizationCodeExchanger>();
        services.AddTransient<IOidcAuthorizationCodeExchanger>(sp => sp.GetRequiredService<OidcAuthorizationCodeExchanger>());

        services.AddTransient<IOidcAuthorizationRequestFactory, OidcAuthorizationRequestFactory>();

        return services;
    }
}
