namespace BitCode.Framework.Platform.IntegrationHub.Conectores;

/// <summary>
/// Mecanismo de autenticación que <see cref="IntegrationConnector"/> aplica a cada llamada saliente
/// (Fase 6, módulo 9: "credenciales" del Plan Maestro). DELIBERADAMENTE acotado a los tres esquemas más
/// comunes de un webhook/API REST simple -- no OAuth2 client-credentials, no mTLS, no firma HMAC por
/// request: un consumidor productivo que necesite un esquema más avanzado debe reemplazar
/// <see cref="Envio.IIntegrationConnectorSender"/> con su propia implementación (ver
/// <c>docs/guia-integration-hub.md</c>, sección "Qué quedó completo y qué no").
/// </summary>
public enum TipoAutenticacionConector
{
    /// <summary>Sin ningún encabezado de autenticación agregado.</summary>
    Ninguna = 0,

    /// <summary>Agrega un encabezado HTTP (<see cref="IntegrationConnector.ApiKeyHeaderName"/>) con el
    /// valor del secreto resuelto vía <see cref="Shared.Infrastructure.Security.Secrets.ISecretProvider"/>.</summary>
    ApiKey = 1,

    /// <summary>Agrega <c>Authorization: Bearer {secreto}</c>, con el token resuelto vía
    /// <see cref="Shared.Infrastructure.Security.Secrets.ISecretProvider"/> -- "estático" porque este
    /// módulo no renueva el token por su cuenta (a diferencia de un flujo OAuth2 client-credentials real):
    /// el consumidor es responsable de mantener actualizado el valor del secreto.</summary>
    BearerEstatico = 2,
}
