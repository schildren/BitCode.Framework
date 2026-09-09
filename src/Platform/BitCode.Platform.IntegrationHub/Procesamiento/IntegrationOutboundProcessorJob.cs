using BitCode.Framework.Platform.IntegrationHub.Conectores;
using BitCode.Framework.Platform.IntegrationHub.Envio;
using BitCode.Framework.Platform.IntegrationHub.Mapping;
using BitCode.Framework.Platform.IntegrationHub.Solicitudes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace BitCode.Framework.Platform.IntegrationHub.Procesamiento;

/// <summary>
/// Procesa periódicamente el lote de <see cref="IntegrationRequest"/> pendientes -- las nuevas
/// (<see cref="IntegrationRequestEstado.PendienteDeEnvio"/>) y las que ya vencieron su backoff
/// (<see cref="IntegrationRequestEstado.PendienteDeReintento"/> con <see cref="IntegrationRequest.ProximoReintentoUtc"/>
/// vencido) -- (Fase 6, módulo 9: "colas" del Plan Maestro). Un consumidor real lo registra con
/// <c>AddSharedBackgroundJobs</c> (F4-11, Quartz HA), mismo patrón exacto que
/// <c>NotificationRetryJob</c> (Fase 6, módulo 8):
/// <code>
/// services.AddSharedBackgroundJobs(
///     quartz =>
///     {
///         var jobKey = new JobKey("integrationhub-outbound");
///         quartz.AddJob&lt;IntegrationOutboundProcessorJob&gt;(j => j.WithIdentity(jobKey).RequestRecovery().StoreDurably());
///         quartz.AddTrigger(t => t.ForJob(jobKey).WithSimpleSchedule(s => s.WithIntervalInSeconds(15).RepeatForever()));
///     },
///     ha => ha.ConnectionString = connectionString);
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Acceso directo a <see cref="IntegrationHubDbContext"/> con <c>IgnoreQueryFilters()</c> -- excepción
/// legítima documentada a la regla dura 1/5 (docs/convenciones.md), mismo precedente que
/// <c>NotificationRetryJob</c>/<c>WorkflowEscalamientoJob</c>/<c>OutboxBatchProcessor</c>: este job es un
/// worker de infraestructura, no un handler de comando dentro del pipeline de MediatR/
/// <c>TransactionBehavior</c>, y necesita ver solicitudes pendientes de TODOS los tenants en cada
/// disparo. Llama <c>SaveChangesAsync</c> explícitamente por la misma razón.
/// </para>
/// <para>
/// <b>Aislamiento por ítem (regla aplicada desde el diseño inicial, no como corrección posterior):</b>
/// cada solicitud del lote se procesa dentro de su propio <c>try/catch</c> -- una excepción no controlada
/// de <see cref="IIntegrationConnectorSender"/> (un sender de un consumidor real, no
/// <see cref="HttpIntegrationConnectorSender"/>, que ya clasifica todos sus fallos) NUNCA debe abortar el
/// <c>foreach</c> completo antes del único <c>SaveChangesAsync</c> final -- ver el hallazgo Alto que la
/// auditoría de arquitectura encontró en <c>NotificationRetryJob</c> (2026-09-09) y que este módulo evita
/// desde el principio.
/// </para>
/// <para>
/// <b>Idempotente por diseño (regla dura 28):</b> si el proceso muere a mitad de este método, la próxima
/// ejecución (<c>RequestRecovery()</c>) vuelve a intentar las mismas filas que sigan
/// <see cref="IntegrationRequestEstado.PendienteDeEnvio"/>/<see cref="IntegrationRequestEstado.PendienteDeReintento"/>
/// con el backoff vencido -- un reintento duplicado contra el conector externo es un duplicado aceptable
/// (semántica "at-least-once", nunca exactly-once de punta a punta, Plan Maestro sección 3.2), no una
/// corrupción de datos.
/// </para>
/// </remarks>
public sealed class IntegrationOutboundProcessorJob(
    IntegrationHubDbContext dbContext, IIntegrationConnectorSender sender, IOptions<IntegrationHubOptions> options)
    : IJob
{
    public Task Execute(IJobExecutionContext context) => ProcesarPendientesAsync(context.CancellationToken);

    /// <summary>Lógica real del job, separada de <see cref="Execute"/> para poder probarla sin construir
    /// un <see cref="IJobExecutionContext"/> real de Quartz -- mismo criterio que
    /// <c>NotificationRetryJob.ReintentarPendientesAsync</c>.</summary>
    public async Task ProcesarPendientesAsync(CancellationToken cancellationToken)
    {
        var ahoraUtc = DateTime.UtcNow;

        var pendientes = await dbContext.IntegrationRequests
            .IgnoreQueryFilters()
            .Where(r => r.Estado == IntegrationRequestEstado.PendienteDeEnvio
                || (r.Estado == IntegrationRequestEstado.PendienteDeReintento
                    && r.ProximoReintentoUtc != null && r.ProximoReintentoUtc <= ahoraUtc))
            .ToListAsync(cancellationToken);

        if (pendientes.Count == 0)
        {
            return;
        }

        // Cache de conectores/mappings dentro de un mismo ciclo -- varias solicitudes pendientes suelen
        // apuntar al mismo conector; evita repetir la misma consulta para cada una.
        var conectoresCache = new Dictionary<Guid, (IntegrationConnector Connector, List<IntegrationFieldMapping> Mappings)>();

        foreach (var integrationRequest in pendientes)
        {
            try
            {
                if (!conectoresCache.TryGetValue(integrationRequest.ConnectorId, out var conectorInfo))
                {
                    var connector = await dbContext.IntegrationConnectors
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.Id == integrationRequest.ConnectorId, cancellationToken);
                    if (connector is null)
                    {
                        // El conector fue eliminado (no hay comando de borrado hoy, pero un consumidor
                        // futuro podría agregarlo) -- permanente: no hay a dónde enviar.
                        RegistrarFalloPermanente(integrationRequest, "El conector referenciado ya no existe.", codigoHttp: null, "(desconocido)");
                        continue;
                    }

                    if (!connector.Activo)
                    {
                        // Conector desactivado temporalmente -- se salta SIN consumir un intento, mismo
                        // criterio que NotificationRetryJob con un canal sin sender registrado: el
                        // próximo ciclo la vuelve a intentar si para entonces el conector se reactivó.
                        continue;
                    }

                    var mappings = await dbContext.IntegrationFieldMappings
                        .IgnoreQueryFilters()
                        .Where(m => m.ConnectorId == connector.Id)
                        .ToListAsync(cancellationToken);

                    conectorInfo = (connector, mappings);
                    conectoresCache[integrationRequest.ConnectorId] = conectorInfo;
                }

                var (conector, mappings2) = conectorInfo;

                var mapeoResult = IntegrationFieldMapper.Map(integrationRequest.PayloadInternoJson, mappings2);
                if (mapeoResult.IsFailure)
                {
                    RegistrarFalloPermanente(integrationRequest, mapeoResult.Error.Description, codigoHttp: null, conector.Codigo);
                    continue;
                }

                integrationRequest.RegistrarPayloadExterno(mapeoResult.Value);

                var intentoNumero = integrationRequest.IntentosRealizados + 1;
                var resultado = await sender.SendAsync(conector, mapeoResult.Value, cancellationToken);
                var timestampUtc = DateTime.UtcNow;

                IntegrationRequestLogResultado logResultado;
                switch (resultado.Outcome)
                {
                    case IntegrationSendOutcome.Exitoso:
                        integrationRequest.RegistrarEnvioExitoso(timestampUtc, resultado.CodigoHttp ?? 0, conector.Codigo);
                        logResultado = IntegrationRequestLogResultado.Exitoso;
                        break;

                    case IntegrationSendOutcome.FalloPermanente:
                        integrationRequest.RegistrarEnvioFallidoPermanente(
                            resultado.ErrorMensaje ?? "Fallo permanente sin detalle.", resultado.CodigoHttp, conector.Codigo);
                        logResultado = IntegrationRequestLogResultado.FalloPermanente;
                        break;

                    default:
                        integrationRequest.RegistrarEnvioFallidoTransitorio(
                            resultado.ErrorMensaje ?? "Fallo transitorio sin detalle.", resultado.CodigoHttp,
                            timestampUtc, options.Value.Retry, conector.Codigo);
                        logResultado = IntegrationRequestLogResultado.FalloTransitorio;
                        break;
                }

                AgregarLog(integrationRequest, intentoNumero, logResultado, resultado.CodigoHttp, resultado.ErrorMensaje);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Aislamiento por ítem -- ver el remarks de esta clase. Se trata como transitorio (mismo
                // criterio conservador que NotificationRetryJob): ante la duda, reintentar es más seguro
                // que descartar definitivamente un envío legítimo.
                var intentoNumero = integrationRequest.IntentosRealizados + 1;
                integrationRequest.RegistrarEnvioFallidoTransitorio(
                    ex.Message, codigoHttp: null, DateTime.UtcNow, options.Value.Retry, "(desconocido)");
                AgregarLog(integrationRequest, intentoNumero, IntegrationRequestLogResultado.FalloTransitorio, null, ex.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void RegistrarFalloPermanente(IntegrationRequest integrationRequest, string error, int? codigoHttp, string connectorCodigo)
    {
        var intentoNumero = integrationRequest.IntentosRealizados + 1;
        integrationRequest.RegistrarEnvioFallidoPermanente(error, codigoHttp, connectorCodigo);
        AgregarLog(integrationRequest, intentoNumero, IntegrationRequestLogResultado.FalloPermanente, codigoHttp, error);
    }

    /// <summary>TenantId asignado explícitamente (no vía TenantSaveChangesInterceptor, que no tiene ningún
    /// tenant "actual" que asumir en un job cross-tenant sin HttpContext) -- se toma del mismo tenant que
    /// la solicitud procesada, leída con IgnoreQueryFilters más arriba. Mismo criterio que
    /// <c>NotificationRetryJob</c> con <c>NotificationDelivery</c>.</summary>
    private void AgregarLog(
        IntegrationRequest integrationRequest, int intentoNumero, IntegrationRequestLogResultado resultado, int? codigoHttp, string? errorMensaje)
    {
        dbContext.IntegrationRequestLogs.Add(new IntegrationRequestLog(
            Guid.NewGuid(), integrationRequest.Id, intentoNumero, resultado, codigoHttp, errorMensaje)
        {
            TenantId = integrationRequest.TenantId,
        });
    }
}
