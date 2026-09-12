using System.Net;
using System.Net.Mail;
using BitCode.Framework.Platform.Notifications.Plantillas;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Platform.Notifications.Envio.Canales;

/// <summary>
/// Implementación de referencia REAL (no un mock/fake) de <see cref="INotificationChannelSender"/> para
/// <see cref="NotificationChannel.Email"/> vía SMTP (<see cref="SmtpClient"/> -- ya en el BCL, sin
/// dependencia externa nueva). Funciona contra cualquier servidor SMTP real, incluido uno de prueba: la
/// suite de integración de este módulo (<c>Sample.Notifications.Api.Tests</c>) lo verifica de punta a
/// punta contra un contenedor SMTP real (smtp4dev, vía Testcontainers, <c>Shared.Testing.SmtpContainerFixture</c>),
/// consultando después su API REST para confirmar que el mensaje efectivamente llegó -- no un mock de
/// <see cref="INotificationChannelSender"/>.
/// </summary>
/// <remarks>
/// <b>Qué tan "real" es este canal, honestamente:</b> es un cliente SMTP real, no una integración con
/// ningún proveedor comercial (SendGrid, Amazon SES, etc.) -- no hay reintentos a nivel de proveedor,
/// tracking de aperturas/clicks, ni gestión de plantillas del lado del proveedor. Además, este módulo NO
/// resuelve la dirección de email de un <c>UserId</c> por su cuenta (no tiene acceso a Identity
/// Administration, Fase 6 módulo 1) -- <see cref="Notification.DestinatarioContacto"/> debe venir ya
/// resuelto del llamador (ver <c>docs/guia-notifications.md</c>, sección "Canales"). Si
/// <see cref="Notification.DestinatarioContacto"/> es <see langword="null"/>/vacío para una notificación
/// de este canal, es un error de PROGRAMACIÓN del llamador (falta de contacto para Email), tratado como
/// fallo PERMANENTE (nunca se resuelve reintentando).
/// <see cref="SmtpClient"/> está marcado obsoleto (<c>SYSLIB0014</c>) por Microsoft a favor de librerías
/// de terceros (MailKit) para escenarios productivos avanzados (OAuth2, pooling de conexiones) -- para
/// el alcance de este corte (SMTP simple, sin autenticación OAuth) sigue siendo funcionalmente correcto y
/// evita introducir una dependencia de paquete nueva solo para esto (ver <c>docs/politica-dependencias.md</c>);
/// un consumidor productivo que necesite OAuth2/pooling avanzado puede reemplazar este registro con su
/// propia implementación de <see cref="INotificationChannelSender"/> sin tocar el resto del módulo.
/// </remarks>
internal sealed class EmailNotificationChannelSender(IOptions<SmtpOptions> options) : INotificationChannelSender
{
    public NotificationChannel Canal => NotificationChannel.Email;

    public async Task<NotificationSendResult> SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(notification.DestinatarioContacto))
        {
            return NotificationSendResult.FalloPermanente(
                "Falta DestinatarioContacto (email) -- el canal Email no puede resolver la dirección de un UserId por su cuenta.");
        }

        var smtpOptions = options.Value;

        try
        {
            // Asunto saneado de CR/LF antes de asignarlo al encabezado -- una plantilla con un valor de
            // placeholder que contenga un salto de línea (dato de usuario sin control de este módulo, ver
            // NotificationTemplateRenderer) no debe poder inyectar encabezados SMTP adicionales. El
            // cuerpo NO se sanea de la misma forma porque un mensaje legítimamente multilínea en el
            // cuerpo no es un vector de inyección de encabezados.
            var asuntoSaneado = (notification.Asunto ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

            // Construcción de MailMessage/SmtpClient DENTRO del try -- MailMessage(string,string) parsea
            // DestinatarioContacto como MailAddress y lanza FormatException si no es una dirección válida
            // (nunca validado como email en EnviarNotificacionCommandValidator, solo longitud máxima);
            // dejar esto fuera del try (como estaba antes) escapaba como excepción no controlada en vez
            // de un Result.Failure clasificado -- hallazgo Alto de auditoría de arquitectura, 2026-09-09.
            using var mensaje = new MailMessage(smtpOptions.FromAddress, notification.DestinatarioContacto)
            {
                Subject = asuntoSaneado,
                Body = notification.Cuerpo,
            };

            using var client = new SmtpClient(smtpOptions.Host, smtpOptions.Port)
            {
                EnableSsl = smtpOptions.EnableSsl,
            };

            if (!string.IsNullOrWhiteSpace(smtpOptions.UserName))
            {
                client.Credentials = new NetworkCredential(smtpOptions.UserName, smtpOptions.Password);
            }

            await client.SendMailAsync(mensaje, cancellationToken);
            return NotificationSendResult.Exitoso();
        }
        catch (FormatException ex)
        {
            // DestinatarioContacto no es una dirección de email sintácticamente válida -- dato de
            // negocio incorrecto, no un problema transitorio de red: reintentar no lo va a resolver.
            return NotificationSendResult.FalloPermanente($"Dirección de email inválida: {ex.Message}");
        }
        catch (SmtpException ex)
        {
            // SmtpException.StatusCode distingue errores permanentes del protocolo (dirección
            // rechazada, buzón inexistente -- 5xx SMTP) de errores transitorios (servidor temporalmente
            // no disponible -- 4xx SMTP); un fallo de conexión (sin StatusCode útil) se trata como
            // transitorio por defecto -- puede ser una caída momentánea del servidor SMTP.
            var esPermanente = ex.StatusCode is
                SmtpStatusCode.MailboxUnavailable or
                SmtpStatusCode.MailboxNameNotAllowed or
                SmtpStatusCode.MailboxBusy;

            return esPermanente
                ? NotificationSendResult.FalloPermanente(ex.Message)
                : NotificationSendResult.FalloTransitorio(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cualquier otro fallo (por ejemplo, el servidor SMTP no responde / DNS) se trata como
            // transitorio -- consistente con el criterio conservador ya documentado para
            // DefaultEventPublishFailureClassifier (F3-07): ante la duda, reintentar es más seguro que
            // descartar definitivamente un envío legítimo.
            return NotificationSendResult.FalloTransitorio(ex.Message);
        }
    }
}
