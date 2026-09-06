using BitCode.Framework.Shared.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Secrets;

/// <summary>
/// Registra <see cref="ISecretProvider"/> (F2-12, Épica F2-C) con el proveedor concreto seleccionado
/// exclusivamente por configuración (<see cref="SecretProviderOptions.Provider"/>, sección
/// <see cref="SecretProviderOptions.SectionName"/>) — mismo principio de intercambiabilidad que
/// <c>AddSharedOidcAuthentication</c> (F2-01, ADR 0004): el código de negocio inyecta
/// <see cref="ISecretProvider"/> y nunca un tipo concreto.
/// </summary>
public static class SecretProviderServiceCollectionExtensions
{
    /// <summary>
    /// Lee <c>"Secrets:Provider"</c> (default <see cref="SecretProviderKind.Configuration"/> si no se
    /// configura, para no bloquear el trabajo local sin un Vault disponible) y registra la implementación
    /// correspondiente de <see cref="ISecretProvider"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="SecretProviderKind.Vault"/> seleccionado sin <c>"Secrets:Vault:Address"</c>/<c>"Secrets:Vault:Token"</c>,
    /// o con <c>Address</c> sin HTTPS y <c>AllowInsecureHttp</c> no habilitado explícitamente.
    /// </exception>
    public static IServiceCollection AddSharedSecretProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var rootSection = configuration.GetSection(SecretProviderOptions.SectionName);
        services.Configure<SecretProviderOptions>(rootSection);
        var provider = rootSection.Get<SecretProviderOptions>()?.Provider ?? SecretProviderKind.Configuration;

        switch (provider)
        {
            case SecretProviderKind.Vault:
                RegisterVaultProvider(services, configuration);
                break;

            case SecretProviderKind.Configuration:
            default:
                RegisterConfigurationProvider(services, configuration);
                break;
        }

        return services;
    }

    private static void RegisterConfigurationProvider(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ConfigurationSecretProviderOptions>(
            configuration.GetSection(ConfigurationSecretProviderOptions.SectionName));
        // ConfigurationSecretProvider lee la clave del secreto en tiempo de resolución (dinámica, no
        // conocida al registrar), así que necesita el propio IConfiguration inyectado -- ASP.NET Core
        // ya lo registra como singleton al construir el host; TryAddSingleton cubre también el caso de
        // un ServiceCollection standalone (pruebas, un consumidor sin Generic Host) sin duplicar el
        // registro si ya existe.
        services.TryAddSingleton(configuration);
        services.AddSingleton<ISecretProvider, ConfigurationSecretProvider>();
    }

    private static void RegisterVaultProvider(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(VaultSecretProviderOptions.SectionName);
        services.Configure<VaultSecretProviderOptions>(section);
        var options = section.Get<VaultSecretProviderOptions>()
            ?? throw new InvalidOperationException(
                $"Falta la sección de configuración '{VaultSecretProviderOptions.SectionName}' (Address/Token) para '{nameof(SecretProviderKind.Vault)}'.");

        if (string.IsNullOrWhiteSpace(options.Address))
        {
            throw new InvalidOperationException(
                $"'{VaultSecretProviderOptions.SectionName}:{nameof(VaultSecretProviderOptions.Address)}' es obligatorio para el proveedor de secretos Vault (F2-12).");
        }

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            throw new InvalidOperationException(
                $"'{VaultSecretProviderOptions.SectionName}:{nameof(VaultSecretProviderOptions.Token)}' es obligatorio para el proveedor de secretos Vault (F2-12) — resolverlo desde una variable de entorno, nunca desde 'appsettings.json'.");
        }

        if (!options.AllowInsecureHttp && !options.Address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{VaultSecretProviderOptions.SectionName}:{nameof(VaultSecretProviderOptions.Address)}' debe usar HTTPS (Zero Trust, Plan Maestro sección 1); habilitar '{nameof(VaultSecretProviderOptions.AllowInsecureHttp)}' únicamente para un Vault de desarrollo local o Testcontainers.");
        }

        // F1-26: mismo timeout/retry/circuit-breaker que cualquier otra llamada HTTP saliente del
        // framework -- la lectura de un secreto no reimplementa resiliencia propia. AddResilientHttpClient
        // registra VaultSecretProvider como cliente HTTP tipado (transient, vía IHttpClientFactory,
        // mismo patrón que AddSharedServiceIdentity/F2-04) -- ISecretProvider se resuelve como transient
        // también, nunca singleton, para no capturar un HttpClient reciclado por el factory.
        services.AddResilientHttpClient<VaultSecretProvider>()
            .ConfigureHttpClient(client => client.BaseAddress = new Uri(options.Address.TrimEnd('/') + "/"));
        services.AddTransient<ISecretProvider>(sp => sp.GetRequiredService<VaultSecretProvider>());
    }
}
