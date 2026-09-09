using BitCode.Framework.Shared.Application.Eventing;

namespace BitCode.Framework.Platform.IntegrationHub;

/// <summary>
/// Opciones del módulo Integration Hub -- sección de configuración <c>"IntegrationHub"</c>.
/// </summary>
/// <remarks>
/// <see cref="Retry"/> reutiliza <see cref="EventRetryPolicyOptions"/> (F3-07) TAL CUAL, sin
/// reimplementar backoff/reintentos -- mismo mecanismo que ya usa <c>NotificationsOptions.Retry</c>
/// (Fase 6, módulo 8). Esta es la capa de retry del <see cref="Solicitudes.IntegrationRequest"/> completo
/// entre ciclos del job (ver el <c>remarks</c> de <c>Envio.HttpIntegrationConnectorSender</c> para la
/// distinción con la resiliencia HTTP de F1-26, que opera dentro de un único intento).
/// </remarks>
public sealed class IntegrationHubOptions
{
    public EventRetryPolicyOptions Retry { get; set; } = new();
}
