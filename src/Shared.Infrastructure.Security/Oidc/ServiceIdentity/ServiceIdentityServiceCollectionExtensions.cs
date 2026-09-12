using BitCode.Framework.Shared.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.ServiceIdentity;

/// <summary>
/// Registra la identidad de servicio de este workload (F2-04, OAuth2 Client Credentials): el resolutor
/// de token (<see cref="IServiceTokenProvider"/>, con cacheo y renovación en <see cref="ServiceTokenCache"/>)
/// y el <see cref="ServiceIdentityAuthenticationHandler"/> que un cliente HTTP tipado puede agregar para
/// adjuntar ese token a sus llamadas salientes. No mapea ningún endpoint HTTP entrante -- a diferencia de
/// F2-02/F2-03, F2-04 es exclusivamente un consumidor de identidad, nunca un emisor.
/// </summary>
public static class ServiceIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Lee la sección de configuración "Oidc:ServiceIdentity" (Authority/ClientId obligatorios, uno de
    /// ClientSecret/CertificateThumbprint obligatorio) y registra los servicios de identidad de servicio.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Falta "Oidc:ServiceIdentity:Authority", "Oidc:ServiceIdentity:ClientId", o tanto
    /// "Oidc:ServiceIdentity:ClientSecret" como "Oidc:ServiceIdentity:CertificateThumbprint".
    /// </exception>
    public static IServiceCollection AddSharedServiceIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(ServiceIdentityOptions.SectionName);
        services.Configure<ServiceIdentityOptions>(section);
        var options = section.Get<ServiceIdentityOptions>()
            ?? throw new InvalidOperationException(
                $"Falta la sección de configuración '{ServiceIdentityOptions.SectionName}' (Authority/ClientId/ClientSecret).");

        if (string.IsNullOrWhiteSpace(options.Authority))
        {
            throw new InvalidOperationException(
                $"'{ServiceIdentityOptions.SectionName}:{nameof(ServiceIdentityOptions.Authority)}' es obligatorio para la identidad de servicio (F2-04).");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new InvalidOperationException(
                $"'{ServiceIdentityOptions.SectionName}:{nameof(ServiceIdentityOptions.ClientId)}' es obligatorio para la identidad de servicio (F2-04).");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret) && string.IsNullOrWhiteSpace(options.CertificateThumbprint))
        {
            throw new InvalidOperationException(
                $"'{ServiceIdentityOptions.SectionName}:{nameof(ServiceIdentityOptions.ClientSecret)}' o '{nameof(ServiceIdentityOptions.CertificateThumbprint)}' es obligatorio para la identidad de servicio (F2-04).");
        }

        services.AddSingleton<ServiceTokenCache>();

        // F1-26: mismos timeout/retry/circuit-breaker que cualquier otra llamada HTTP saliente del
        // framework -- ni la resolución de metadata ni la solicitud de token reimplementan resiliencia.
        services.AddResilientHttpClient<ServiceTokenProvider>();
        services.AddTransient<IServiceTokenProvider>(sp => sp.GetRequiredService<ServiceTokenProvider>());

        // Handler transient estándar de IHttpClientFactory: cada cliente tipado que lo agrega
        // (AddServiceIdentityAuthentication) resuelve su propia instancia, pero todas comparten el mismo
        // IServiceTokenProvider/ServiceTokenCache singleton -- un único token de servicio reutilizado por
        // todas las llamadas salientes de este workload, no uno por cliente tipado.
        services.AddTransient<ServiceIdentityAuthenticationHandler>();

        return services;
    }

    /// <summary>
    /// Agrega <see cref="ServiceIdentityAuthenticationHandler"/> a la pipeline de un cliente HTTP tipado
    /// ya registrado (típicamente vía <c>AddResilientHttpClient</c>, F1-26) para que adjunte
    /// automáticamente el token de identidad de servicio de este workload a cada request saliente.
    /// Requiere haber llamado antes a <see cref="AddSharedServiceIdentity"/>.
    /// </summary>
    public static IHttpClientBuilder AddServiceIdentityAuthentication(this IHttpClientBuilder builder) =>
        builder.AddHttpMessageHandler<ServiceIdentityAuthenticationHandler>();
}
