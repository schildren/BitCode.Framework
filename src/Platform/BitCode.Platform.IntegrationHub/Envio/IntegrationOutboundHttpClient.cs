namespace BitCode.Framework.Platform.IntegrationHub.Envio;

/// <summary>
/// Envoltorio mínimo de <see cref="System.Net.Http.HttpClient"/> registrado como cliente HTTP TIPADO
/// (<c>AddResilientHttpClient&lt;IntegrationOutboundHttpClient&gt;</c>, F1-26) -- mismo patrón que
/// <c>VaultSecretProvider</c> (F2-12). No fija <c>BaseAddress</c> en el registro (a diferencia de
/// <c>VaultSecretProvider</c>, que sí conoce su dirección al momento de registrarse): la URL de destino de
/// Integration Hub es DINÁMICA (una por <see cref="Conectores.IntegrationConnector"/>, resuelta recién en
/// el momento de enviar), así que <see cref="HttpIntegrationConnectorSender"/> construye el
/// <see cref="System.Uri"/> absoluto de cada request explícitamente.
/// </summary>
public sealed class IntegrationOutboundHttpClient(HttpClient httpClient)
{
    public HttpClient HttpClient { get; } = httpClient;
}
