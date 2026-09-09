namespace BitCode.Framework.Platform.IntegrationHub.Solicitudes;

public enum IntegrationRequestEstado
{
    PendienteDeEnvio = 0,
    Enviada = 1,
    PendienteDeReintento = 2,
    Fallida = 3,
}
