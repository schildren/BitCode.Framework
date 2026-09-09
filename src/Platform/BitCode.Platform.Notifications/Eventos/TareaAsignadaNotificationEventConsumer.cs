using BitCode.Framework.Platform.Notifications.Envio;
using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application.Eventing;

namespace BitCode.Framework.Platform.Notifications.Eventos;

/// <summary>
/// EJEMPLO DE REFERENCIA de "cómo enganchar un consumidor nuevo" a este mecanismo genérico (Fase 6,
/// módulo 8, dependencia declarada del Plan Maestro: Events, no Workflow puntualmente) -- consume
/// <see cref="TareaAsignadaIntegrationEvent"/> (Fase 6, módulo 6, Workflow) y dispara una notificación
/// InApp con la plantilla lógica <c>"tarea-asignada"</c> hacia <see cref="TareaAsignadaIntegrationEvent.AsignadoAUserId"/>.
/// Mismo mecanismo exacto que ya usa Task Inbox (Fase 6, módulo 7) para el mismo evento -- ambos módulos
/// son consumidores independientes, registrados en <c>docs/catalogo-eventos.md</c> como dos filas
/// separadas de "Consumidores conocidos" de <c>Workflow.TareaAsignada</c>.
/// </summary>
/// <remarks>
/// <b>Por qué SOLO InApp, honestamente:</b> <see cref="TareaAsignadaIntegrationEvent"/> no transporta
/// ningún dato de contacto (email) del <c>AsignadoAUserId</c> -- solo su <c>Guid</c>. El canal InApp no
/// necesita esa información (<see cref="Envio.Canales.InAppNotificationChannelSender"/> no depende de
/// ningún dato de contacto externo), así que es el único que este consumidor puede ejercitar de punta a
/// punta sin acoplarse a Identity Administration. Un consumidor productivo que quisiera además avisar
/// por email necesitaría, o bien enriquecer <see cref="TareaAsignadaIntegrationEvent"/> con el email del
/// asignado (cambio de contrato en Workflow, fuera de alcance de este módulo), o bien resolver el email
/// consultando la API pública de Identity Administration ANTES de llamar a <see cref="INotificationSender"/>
/// (acoplamiento síncrono adicional que este ejemplo decide NO introducir).
///
/// <b>Plantilla requerida:</b> este consumidor asume que existe una <see cref="NotificationTemplate"/>
/// activa con código <c>"tarea-asignada"</c>, canal <see cref="NotificationChannel.InApp"/> y locale
/// <c>"es-AR"</c> -- si no existe, <see cref="INotificationSender.EnviarAsync"/> devuelve un
/// <see cref="Shared.Kernel.Result{TValue}"/> fallido y este método lo propaga como excepción: el
/// mecanismo de Inbox (F1-24/F3-04) NO marca el mensaje como procesado (el <c>handler</c> lanzó), así
/// que una reentrega posterior (o una vez que un operador cree la plantilla faltante) sí lo procesa --
/// mismo criterio de "reinicio no pierde eventos" que el resto de la plataforma, nunca un descarte
/// silencioso de un evento de negocio real.
/// </remarks>
internal sealed class TareaAsignadaNotificationEventConsumer(INotificationSender sender)
    : IEventConsumer<TareaAsignadaIntegrationEvent>
{
    internal const string CodigoPlantilla = "tarea-asignada";
    internal const string LocalePorDefecto = "es-AR";

    public async Task ConsumeAsync(TareaAsignadaIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var resultado = await sender.EnviarAsync(
            new EnviarNotificacionRequest(
                DestinatarioUserId: integrationEvent.AsignadoAUserId,
                DestinatarioContacto: null,
                // DisparadoPorUserId: null -- el origen es un evento de integración, no una acción
                // humana explícita (ver el remarks de EnviarNotificacionRequest).
                DisparadoPorUserId: null,
                CodigoPlantilla: CodigoPlantilla,
                Canal: NotificationChannel.InApp,
                Locale: LocalePorDefecto,
                Datos: new Dictionary<string, string>
                {
                    ["workflowTaskId"] = integrationEvent.WorkflowTaskId.ToString(),
                    ["workflowInstanceId"] = integrationEvent.WorkflowInstanceId.ToString(),
                }),
            cancellationToken);

        if (resultado.IsFailure)
        {
            throw new InvalidOperationException(
                $"No se pudo disparar la notificación 'tarea-asignada' para la tarea {integrationEvent.WorkflowTaskId}: " +
                $"{resultado.Error.Code} -- {resultado.Error.Description}");
        }
    }
}
