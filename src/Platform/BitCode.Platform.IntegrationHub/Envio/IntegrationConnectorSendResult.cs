namespace BitCode.Framework.Platform.IntegrationHub.Envio;

public sealed record IntegrationConnectorSendResult(IntegrationSendOutcome Outcome, int? CodigoHttp, string? ErrorMensaje)
{
    public static IntegrationConnectorSendResult Exitoso(int codigoHttp) => new(IntegrationSendOutcome.Exitoso, codigoHttp, null);

    public static IntegrationConnectorSendResult FalloTransitorio(string errorMensaje, int? codigoHttp = null) =>
        new(IntegrationSendOutcome.FalloTransitorio, codigoHttp, errorMensaje);

    public static IntegrationConnectorSendResult FalloPermanente(string errorMensaje, int? codigoHttp = null) =>
        new(IntegrationSendOutcome.FalloPermanente, codigoHttp, errorMensaje);
}
