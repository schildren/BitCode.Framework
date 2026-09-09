using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.IntegrationHub.Solicitudes;

/// <summary>
/// Tracking (Fase 6, módulo 9: "monitoreo" del Plan Maestro) append-only de CADA intento de llamada al
/// conector externo -- distinto del estado agregado de <see cref="IntegrationRequest"/> (que solo
/// refleja el ÚLTIMO intento): esta tabla es el historial completo, mismo espíritu que
/// <c>NotificationDelivery</c> (Fase 6, módulo 8) frente al estado agregado de <c>Notification</c>. Nunca
/// se actualiza ni se borra una fila ya escrita.
/// </summary>
public sealed class IntegrationRequestLog : Entity<Guid>, ITenantEntity
{
    public Guid IntegrationRequestId { get; private set; }

    /// <summary>1-based: el intento original cuenta como intento número 1, no 0.</summary>
    public int IntentoNumero { get; private set; }

    public DateTime TimestampUtc { get; private set; }

    public IntegrationRequestLogResultado Resultado { get; private set; }

    /// <summary><see langword="null"/> si la llamada no llegó a completarse (por ejemplo, una excepción
    /// de red antes de recibir cualquier respuesta HTTP).</summary>
    public int? CodigoHttp { get; private set; }

    public string? ErrorMensaje { get; private set; }

    public Guid TenantId { get; set; }

    public IntegrationRequestLog(
        Guid id, Guid integrationRequestId, int intentoNumero, IntegrationRequestLogResultado resultado,
        int? codigoHttp, string? errorMensaje)
        : base(id)
    {
        IntegrationRequestId = integrationRequestId;
        IntentoNumero = intentoNumero;
        TimestampUtc = DateTime.UtcNow;
        Resultado = resultado;
        CodigoHttp = codigoHttp;
        ErrorMensaje = errorMensaje;
    }

    private IntegrationRequestLog()
    {
    }
}
