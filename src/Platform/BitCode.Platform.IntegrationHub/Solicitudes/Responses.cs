namespace BitCode.Framework.Platform.IntegrationHub.Solicitudes;

internal sealed record IntegrationRequestResponse(
    Guid Id,
    Guid ConnectorId,
    Guid? DisparadoPorUserId,
    string PayloadInternoJson,
    string? PayloadExternoJson,
    IntegrationRequestEstado Estado,
    int IntentosRealizados,
    string? UltimoErrorMensaje,
    int? UltimoCodigoHttp,
    DateTime? EnviadaAtUtc);

internal sealed record IntegrationRequestLogResponse(
    Guid Id, int IntentoNumero, DateTime TimestampUtc, IntegrationRequestLogResultado Resultado, int? CodigoHttp, string? ErrorMensaje);
