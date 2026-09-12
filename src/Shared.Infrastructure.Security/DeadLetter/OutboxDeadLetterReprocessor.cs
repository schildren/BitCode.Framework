using BitCode.Framework.Shared.Domain.Outbox;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Shared.Infrastructure.Security.DeadLetter;

/// <summary>
/// Implementación de <see cref="IDeadLetterReprocessor"/> (F3-08) sobre <c>OutboxMessage</c> (F1-23):
/// reabre una fila agotada por el relay de Outbox (F3-07, <c>OutboxBatchProcessor</c>) y deja auditoría
/// de la operación (F2-15, Épica F2-D).
/// </summary>
/// <remarks>
/// <para>
/// <b>Orden "auditar primero, mutar después" (decisión deliberada):</b> este tipo escribe la entrada de
/// auditoría ANTES de tocar <c>OutboxMessage</c>. Si <see cref="IAuditWriter.WriteAsync"/> falla, la fila
/// NUNCA se reabre — nunca puede existir un reprocesamiento sin su registro de auditoría correspondiente
/// (la mitad "auditado" del criterio de aceptación literal de F3-08, "Reprocesamiento auditado", no es
/// negociable). El costo de esta decisión es el inverso: si <c>SaveChangesAsync</c> fallara DESPUÉS de
/// auditar con éxito (una condición de carrera de infraestructura, no de negocio — por ejemplo, SQL
/// Server cae en el intervalo entre ambas escrituras), quedaría una entrada de auditoría de una operación
/// que en los hechos no llegó a aplicarse; se evaluó y se aceptó ese riesgo residual (documentado acá, no
/// oculto) porque el escenario inverso — mutar sin auditar — viola directamente el criterio de aceptación
/// de esta tarea, mientras que el escenario aceptado solo produce un registro de auditoría "de más" que
/// un operador puede correlacionar con el estado real de <c>OutboxMessage.ExhaustedAtUtc</c> (que seguiría
/// no siendo <see langword="null"/> en ese caso).
/// </para>
/// <para>
/// <b>Qué se resetea:</b> <c>ExhaustedAtUtc</c> y <c>Error</c> vuelven a <see langword="null"/>,
/// <c>LockedUntilUtc</c>/<c>LockedBy</c> se limpian (para que el próximo ciclo de sondeo de
/// <c>OutboxBatchProcessor.ClaimBatchAsync</c> pueda reclamar la fila de inmediato, sin esperar ningún
/// backoff previo) y <c>RetryCount</c> vuelve a 0 -- un reprocesamiento explícito, pedido por un operador
/// con un motivo justificado (<see cref="DeadLetterReprocessRequest.Reason"/>), es una decisión consciente
/// de "dale margen completo de nuevo", no una continuación del conteo de intentos que ya se agotó.
/// </para>
/// </remarks>
public sealed class OutboxDeadLetterReprocessor(
    DbContext context,
    IAuditWriter auditWriter,
    ILogger<OutboxDeadLetterReprocessor> logger) : IDeadLetterReprocessor
{
    public async Task<Result<DeadLetterReprocessResult>> ReprocessAsync(
        DeadLetterReprocessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var message = await context.Set<OutboxMessage>()
            .IgnoreQueryFilters() // F3-08: reprocesar es una operación de plataforma, no atada al tenant del scope actual (igual que ClaimBatchAsync, F3-03).
            .FirstOrDefaultAsync(m => m.Id == request.OutboxMessageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
        {
            return Result.Failure<DeadLetterReprocessResult>(Error.NotFound(
                "DeadLetter.OutboxMessageNotFound",
                $"No existe un OutboxMessage con Id '{request.OutboxMessageId}'."));
        }

        if (message.ExhaustedAtUtc is null)
        {
            return Result.Failure<DeadLetterReprocessResult>(Error.Conflict(
                "DeadLetter.NotExhausted",
                $"El OutboxMessage '{request.OutboxMessageId}' no está agotado (ExhaustedAtUtc es null) -- no aplica reprocesamiento de DLQ."));
        }

        var auditRequest = new AuditEntryRequest(
            request.Actor,
            tenantId: message.TenantId,
            action: "OutboxMessage.DeadLetterReprocess",
            resource: new AuditResource("OutboxMessage", message.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: request.Reason,
            correlationId: request.CorrelationId,
            traceId: request.TraceId,
            ipAddress: request.IpAddress,
            metadata: new Dictionary<string, string?>
            {
                ["EventType"] = message.EventType,
                ["PreviousRetryCount"] = message.RetryCount.ToString(),
                ["PreviousError"] = message.Error,
                ["PreviousExhaustedAtUtc"] = message.ExhaustedAtUtc?.ToString("O"),
            });

        var auditResult = await auditWriter.WriteAsync(auditRequest, cancellationToken).ConfigureAwait(false);
        if (auditResult.IsFailure)
        {
            logger.LogWarning(
                "No se pudo auditar el reprocesamiento de OutboxMessage {OutboxMessageId} ({ErrorCode}): {ErrorDescription}. " +
                "La fila NO se reabre -- un reprocesamiento sin auditoría no está permitido (F3-08).",
                message.Id,
                auditResult.Error.Code,
                auditResult.Error.Description);

            return Result.Failure<DeadLetterReprocessResult>(auditResult.Error);
        }

        message.ExhaustedAtUtc = null;
        message.Error = null;
        message.LockedUntilUtc = null;
        message.LockedBy = null;
        message.RetryCount = 0;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "OutboxMessage {OutboxMessageId} reprocesado desde DLQ por {ActorType}:{ActorId} (auditoría {AuditEntryId}). Motivo: {Reason}",
            message.Id,
            request.Actor.Type,
            request.Actor.Id,
            auditResult.Value.Id,
            request.Reason);

        return Result.Success(new DeadLetterReprocessResult
        {
            OutboxMessageId = message.Id,
            NewRetryCount = message.RetryCount,
            AuditEntryId = auditResult.Value.Id,
        });
    }
}
