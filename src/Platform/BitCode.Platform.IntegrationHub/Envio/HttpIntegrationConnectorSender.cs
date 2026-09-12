using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BitCode.Framework.Platform.IntegrationHub.Conectores;
using BitCode.Framework.Shared.Infrastructure.Security.Secrets;

namespace BitCode.Framework.Platform.IntegrationHub.Envio;

/// <summary>
/// Implementación de referencia REAL (no un mock/fake) de <see cref="IIntegrationConnectorSender"/> --
/// llama al endpoint HTTP externo configurado por <see cref="IntegrationConnector"/>. Funciona contra
/// cualquier servidor HTTP real (incluido uno de prueba embebido en el mismo proceso, ver
/// <c>Sample.IntegrationHub.Api.Tests</c>), no un adapter productivo hacia un sistema comercial concreto
/// (ver el <c>remarks</c> de <see cref="IntegrationConnector"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Dos capas de resiliencia, no una sola (ver Plan Maestro, Fase 6 módulo 9, dependencia
/// "Resilience"):</b> <see cref="IntegrationOutboundHttpClient"/> se registra con la pipeline de
/// resiliencia HTTP estándar del framework (F1-26: timeout por intento/total, circuit breaker, bulkhead,
/// y retry — pero el retry de Polly SOLO se activa para métodos que
/// <c>HttpRetrySafety.IsSafeToRetry</c> considera seguros de repetir automáticamente sin duplicar un
/// efecto de negocio: <see cref="MetodoHttpConector.Put"/> lo es por especificación HTTP,
/// <see cref="MetodoHttpConector.Post"/>/<see cref="MetodoHttpConector.Patch"/> NO — este sender
/// DELIBERADAMENTE no los marca <c>MarkSafeToRetry()</c>, porque hacerlo sería asumir, sin ninguna
/// evidencia, que CUALQUIER endpoint externo configurado por un conector deduplica solicitudes por su
/// cuenta). Para un conector POST/PATCH, entonces, la resiliencia de "reintentar una llamada transitoria"
/// no ocurre en esta capa — ocurre en la capa SIGUIENTE, un ciclo completo después:
/// <c>Procesamiento.IntegrationOutboundProcessorJob</c> reintenta la <see cref="Solicitudes.IntegrationRequest"/>
/// completa más tarde (backoff de <c>EventRetryBackoff</c>, F3-07), aceptando la misma semántica
/// "at-least-once" (nunca exactly-once, Plan Maestro sección 3.2) que ya documenta Notifications.
/// </para>
/// </remarks>
internal sealed class HttpIntegrationConnectorSender(IntegrationOutboundHttpClient httpClient, ISecretProvider secretProvider)
    : IIntegrationConnectorSender
{
    public async Task<IntegrationConnectorSendResult> SendAsync(
        IntegrationConnector connector, string payloadExternoJson, CancellationToken cancellationToken = default)
    {
        try
        {
            // Uri/HttpRequestMessage construidos DENTRO del try -- new Uri(string, UriKind.Absolute) lanza
            // UriFormatException si BaseUrl quedó mal configurado (dato de negocio inválido, no un fallo de
            // red); dejar esta construcción fuera del try escaparía como excepción no controlada en vez de
            // un resultado clasificado -- mismo hallazgo que ya corrigió Notifications (Fase 6, módulo 8,
            // EmailNotificationChannelSender) para MailMessage/DestinatarioContacto.
            var uri = new Uri(connector.BaseUrl, UriKind.Absolute);
            using var httpRequest = new HttpRequestMessage(MapMetodo(connector.Metodo), uri)
            {
                Content = new StringContent(payloadExternoJson, Encoding.UTF8, "application/json"),
            };

            var authResult = await AplicarAutenticacionAsync(httpRequest, connector, cancellationToken);
            if (authResult is not null)
            {
                return authResult;
            }

            using var response = await httpClient.HttpClient.SendAsync(httpRequest, cancellationToken);
            var codigoHttp = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return IntegrationConnectorSendResult.Exitoso(codigoHttp);
            }

            // 408 (timeout del servidor) y 429 (rate limit) son transitorios por naturaleza; el resto de
            // los 4xx son errores del cliente (payload/URL/autenticación mal configurados) que reintentar
            // sin cambios no va a resolver -- mismo criterio de clasificación 4xx/5xx que
            // EmailNotificationChannelSender aplica a SmtpStatusCode.
            var esTransitorio = response.StatusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or >= HttpStatusCode.InternalServerError;

            var mensaje = $"El conector respondió {codigoHttp} ({response.ReasonPhrase}).";
            return esTransitorio
                ? IntegrationConnectorSendResult.FalloTransitorio(mensaje, codigoHttp)
                : IntegrationConnectorSendResult.FalloPermanente(mensaje, codigoHttp);
        }
        catch (UriFormatException ex)
        {
            return IntegrationConnectorSendResult.FalloPermanente($"BaseUrl del conector inválida: {ex.Message}");
        }
        catch (FormatException ex)
        {
            // HttpHeaders.Add/AuthenticationHeaderValue validan sintaxis de nombre/valor de encabezado y
            // lanzan FormatException si ApiKeyHeaderName (configurado libremente por el usuario, solo
            // MaximumLength(128) validado) o el secreto resuelto contienen caracteres inválidos para un
            // encabezado HTTP -- error de CONFIGURACIÓN del conector, no un fallo de red: sin este catch
            // dedicado, caía en el catch-all de abajo y se clasificaba como transitorio, causando que
            // IntegrationOutboundProcessorJob reintentara indefinidamente (hasta agotar MaxAttempts) una
            // IntegrationRequest contra un conector que nunca va a poder enviar nada (hallazgo Alto de
            // auditoría de arquitectura, 2026-09-09).
            return IntegrationConnectorSendResult.FalloPermanente($"Configuración de autenticación del conector inválida: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cualquier otro fallo (el host no responde / DNS / conexión rechazada) se trata como
            // transitorio -- mismo criterio conservador que el catch-all de EmailNotificationChannelSender.
            return IntegrationConnectorSendResult.FalloTransitorio(ex.Message);
        }
    }

    private async Task<IntegrationConnectorSendResult?> AplicarAutenticacionAsync(
        HttpRequestMessage httpRequest, IntegrationConnector connector, CancellationToken cancellationToken)
    {
        if (connector.TipoAutenticacion == TipoAutenticacionConector.Ninguna)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(connector.SecretKey))
        {
            // Error de configuración del conector (debería haberse rechazado en CrearConectorCommandValidator,
            // pero un dato pudo corromperse entre el alta y este envío) -- permanente: reintentar sin
            // arreglar la configuración no cambia nada.
            return IntegrationConnectorSendResult.FalloPermanente(
                $"El conector requiere autenticación ({connector.TipoAutenticacion}) pero no tiene SecretKey configurada.");
        }

        var secretResult = await secretProvider.GetSecretAsync(connector.SecretKey, cancellationToken);
        if (secretResult.IsFailure)
        {
            // Un secreto no resuelto (Vault caído, clave inexistente) es TRANSITORIO si el proveedor no
            // está disponible, pero esta capa no distingue esos casos entre sí -- se trata como
            // transitorio por defecto: ante la duda, reintentar en el próximo ciclo es más seguro que
            // descartar definitivamente una solicitud legítima por una falla momentánea del proveedor de
            // secretos.
            return IntegrationConnectorSendResult.FalloTransitorio(
                $"No se pudo resolver el secreto '{connector.SecretKey}': {secretResult.Error.Description}");
        }

        switch (connector.TipoAutenticacion)
        {
            case TipoAutenticacionConector.ApiKey:
                httpRequest.Headers.Add(connector.ApiKeyHeaderName ?? "X-Api-Key", secretResult.Value);
                break;

            case TipoAutenticacionConector.BearerEstatico:
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretResult.Value);
                break;
        }

        return null;
    }

    private static HttpMethod MapMetodo(MetodoHttpConector metodo) => metodo switch
    {
        MetodoHttpConector.Post => HttpMethod.Post,
        MetodoHttpConector.Put => HttpMethod.Put,
        MetodoHttpConector.Patch => HttpMethod.Patch,
        _ => HttpMethod.Post,
    };
}
