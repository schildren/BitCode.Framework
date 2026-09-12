using BitCode.Framework.Platform.Notifications.Plantillas;

namespace BitCode.Framework.Platform.Notifications.Envio;

internal sealed record NotificationResponse(
    Guid Id,
    Guid DestinatarioUserId,
    Guid? DisparadoPorUserId,
    string CodigoPlantilla,
    NotificationChannel Canal,
    string Locale,
    string? Asunto,
    string Cuerpo,
    NotificationEstado Estado,
    int IntentosRealizados,
    string? UltimoErrorMensaje,
    DateTime? EnviadaAtUtc,
    DateTime? LeidoAtUtc);
