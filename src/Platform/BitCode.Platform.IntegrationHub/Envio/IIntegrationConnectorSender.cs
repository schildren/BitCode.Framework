using BitCode.Framework.Platform.IntegrationHub.Conectores;

namespace BitCode.Framework.Platform.IntegrationHub.Envio;

/// <summary>
/// Abstracción del "envío real" de un payload externo ya mapeado hacia un
/// <see cref="IntegrationConnector"/> -- un consumidor productivo que necesite un protocolo distinto de
/// HTTP/JSON (SOAP, SFTP, un SDK propietario de un ESB) reemplaza esta interfaz con su propia
/// implementación sin tocar <see cref="Solicitudes.IntegrationRequest"/> ni
/// <c>Procesamiento.IntegrationOutboundProcessorJob</c>.
/// </summary>
public interface IIntegrationConnectorSender
{
    Task<IntegrationConnectorSendResult> SendAsync(
        IntegrationConnector connector, string payloadExternoJson, CancellationToken cancellationToken = default);
}
