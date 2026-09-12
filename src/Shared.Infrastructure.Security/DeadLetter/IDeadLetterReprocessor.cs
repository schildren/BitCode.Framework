using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.DeadLetter;

/// <summary>
/// Reprocesamiento auditado de un mensaje agotado del relay de Outbox (F3-07/F3-08): reabre a
/// <c>OutboxMessage</c> para que <c>OutboxBatchProcessor</c> (<c>Shared.Infrastructure.Persistence</c>)
/// vuelva a intentar publicarlo en el próximo ciclo de sondeo, y deja un registro de auditoría
/// (<see cref="IAuditWriter"/>, Épica F2-D) de quién lo pidió, cuándo y por qué — criterio de aceptación
/// literal de F3-08 ("Reprocesamiento auditado").
/// </summary>
/// <remarks>
/// Vive en <c>Shared.Infrastructure.Security</c> (no en <c>Shared.Infrastructure.Persistence</c>, donde
/// vive <c>OutboxBatchProcessor</c>) porque necesita <see cref="IAuditWriter"/>: este proyecto ya
/// referencia <c>Shared.Infrastructure.Persistence</c> (para <c>MultiTenantIdentityDbContext</c>, F2-01),
/// nunca al revés — introducir la referencia inversa crearía un ciclo de proyectos. Reprocesar sin dejar
/// auditoría es, para este backlog, un "operación privilegiada" (Épica F2-C) sin su control asociado, por
/// lo que este tipo de reprocesamiento pertenece naturalmente junto al resto del módulo de seguridad, no
/// junto al relay que solo sabe publicar/reintentar.
/// </remarks>
public interface IDeadLetterReprocessor
{
    /// <summary>
    /// Reabre el <c>OutboxMessage</c> identificado por <see cref="DeadLetterReprocessRequest.OutboxMessageId"/>
    /// (limpia <c>ExhaustedAtUtc</c>/<c>Error</c>/<c>LockedUntilUtc</c> y reinicia <c>RetryCount</c>) y
    /// escribe la entrada de auditoría correspondiente ANTES de aplicar el cambio — si la escritura de
    /// auditoría falla, la fila NUNCA se reabre (ver remarks de la implementación concreta): no puede
    /// existir un reprocesamiento sin su rastro de auditoría.
    /// </summary>
    Task<Result<DeadLetterReprocessResult>> ReprocessAsync(
        DeadLetterReprocessRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Datos de entrada para <see cref="IDeadLetterReprocessor.ReprocessAsync"/>.</summary>
public sealed class DeadLetterReprocessRequest
{
    public DeadLetterReprocessRequest(Guid outboxMessageId, AuditActor actor, string reason)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        OutboxMessageId = outboxMessageId;
        Actor = actor;
        Reason = reason;
    }

    /// <summary><c>OutboxMessage.Id</c> de la fila agotada que se quiere reprocesar.</summary>
    public Guid OutboxMessageId { get; }

    /// <summary>Quién pide el reprocesamiento — persistido en la entrada de auditoría.</summary>
    public AuditActor Actor { get; }

    /// <summary>
    /// Motivo del reprocesamiento (obligatorio) — por ejemplo, "causa raíz del broker corregida,
    /// confirmado con el equipo de infraestructura". Queda en <see cref="Kernel.Error"/>... en la
    /// entrada de auditoría (<c>AuditEntryRequest.Reason</c>), no es opcional: reprocesar un mensaje que
    /// falló definitivamente es una operación privilegiada (Épica F2-C) que siempre debe poder
    /// justificarse.
    /// </summary>
    public string Reason { get; }

    public string? CorrelationId { get; init; }

    public string? TraceId { get; init; }

    public string? IpAddress { get; init; }
}

/// <summary>Resultado de un reprocesamiento exitoso.</summary>
public sealed class DeadLetterReprocessResult
{
    public required Guid OutboxMessageId { get; init; }

    /// <summary><c>OutboxMessage.RetryCount</c> después de reabrir la fila (siempre 0: margen completo de reintentos otra vez).</summary>
    public required int NewRetryCount { get; init; }

    /// <summary>Id de la entrada de auditoría escrita para esta operación.</summary>
    public required Guid AuditEntryId { get; init; }
}
