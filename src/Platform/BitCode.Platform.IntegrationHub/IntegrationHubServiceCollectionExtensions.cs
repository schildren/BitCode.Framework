using BitCode.Framework.Platform.IntegrationHub.Actors;
using BitCode.Framework.Platform.IntegrationHub.Envio;
using BitCode.Framework.Platform.IntegrationHub.HealthChecks;
using BitCode.Framework.Shared.Infrastructure.Http;
using BitCode.Framework.Shared.Infrastructure.Http.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.IntegrationHub;

/// <summary>
/// Envuelve el cableado de aplicación del módulo Integration Hub (Fase 6, módulo 9) -- mismo espíritu que
/// <c>NotificationsServiceCollectionExtensions</c>. Un consumidor real llama
/// <c>services.AddSharedPersistence&lt;IntegrationHubDbContext&gt;(connectionString)</c> directamente en
/// su propio <c>InfrastructureModule</c> (ver <c>Sample.IntegrationHub.Api</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Requiere <c>AddSharedSecretProvider</c> (F2-12) ya registrado</b> antes de llamar a este método:
/// <see cref="Envio.HttpIntegrationConnectorSender"/> resuelve
/// <see cref="Shared.Infrastructure.Security.Secrets.ISecretProvider"/> del contenedor de DI para
/// autenticar cada llamada saliente -- mismo criterio de dependencia explícita que
/// <c>EncryptionServiceCollectionExtensions.AddSharedEncryption</c> con el mismo proveedor.
/// </para>
/// <para>
/// <b>Por qué este módulo no necesitó además <c>IEncryptionProvider</c> (F2-13):</b> el único dato
/// sensible propio de este módulo es la referencia de credencial de un conector
/// (<see cref="Conectores.IntegrationConnector.SecretKey"/>), y esa es exactamente el tipo de dato para el
/// que <c>ISecretProvider</c> ya es la abstracción correcta (una CLAVE que resuelve un secreto externo,
/// no un valor propio que este módulo necesite cifrar en su propia base) -- agregar
/// <c>IEncryptionProvider</c> sin un campo concreto que lo necesite habría sido cripto sin propósito
/// (ver <c>docs/politica-criptografica.md</c>). Si un consumidor real de este módulo agrega, por ejemplo,
/// un token de callback propio que SÍ necesite cifrado en reposo, debe reutilizar
/// <c>AddSharedEncryption</c>/<c>IEncryptionProvider</c> (F2-13) para esa extensión -- documentado
/// honestamente en <c>docs/guia-integration-hub.md</c>, sección "Credenciales".
/// </para>
/// </remarks>
public static class IntegrationHubServiceCollectionExtensions
{
    /// <param name="configureHttpResilience">
    /// Configuración opcional de la pipeline de resiliencia HTTP saliente (F1-26) del cliente tipado que
    /// llama a los conectores -- si no se provee, aplican los valores por defecto de
    /// <see cref="HttpResilienceOptions"/>.
    /// </param>
    /// <param name="configureOptions">
    /// Configuración opcional de <see cref="IntegrationHubOptions"/> (política de reintentos del
    /// <see cref="Solicitudes.IntegrationRequest"/> completo, F3-07 reutilizado) -- si no se provee,
    /// aplican los valores por defecto de <c>EventRetryPolicyOptions</c>.
    /// </param>
    public static IServiceCollection AddSharedIntegrationHub(
        this IServiceCollection services,
        Action<HttpResilienceOptions>? configureHttpResilience = null,
        Action<IntegrationHubOptions>? configureOptions = null)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IIntegrationHubActorContext, HttpContextIntegrationHubActorContext>();

        services.AddOptions<IntegrationHubOptions>().Configure(configureOptions ?? (_ => { }));

        services.AddResilientHttpClient<IntegrationOutboundHttpClient>(configureHttpResilience ?? (_ => { }));
        services.TryAddScoped<IIntegrationConnectorSender, HttpIntegrationConnectorSender>();

        services.AddHealthChecks()
            .AddCheck<IntegrationHubDbContextHealthCheck>("sql-server-integrationhub", tags: ["ready"]);

        return services;
    }
}
