using System.Text;
using System.Text.Json;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;

/// <summary>
/// Relay de Outbox (F3-03): lee por lotes las filas <see cref="OutboxMessage"/> pendientes
/// (<c>ProcessedAtUtc IS NULL</c>), las bloquea para que dos instancias concurrentes de este proceso
/// nunca publiquen la misma fila dos veces en simultáneo, reconstruye el <see cref="IIntegrationEvent"/>
/// correspondiente a cada una y lo publica vía <see cref="IEventPublisher"/> — solo marca la fila como
/// procesada (<see cref="OutboxMessage.ProcessedAtUtc"/>) DESPUÉS de que la publicación tuvo éxito.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bloqueo (evitar publicación duplicada entre réplicas):</b> <see cref="ClaimBatchAsync"/> ejecuta
/// una única sentencia SQL (CTE + <c>UPDATE ... OUTPUT</c>) contra la tabla de <see cref="OutboxMessage"/>
/// con las pistas de bloqueo <c>UPDLOCK, ROWLOCK, READPAST</c>: cada fila que una instancia ya tiene
/// bloqueada (fila con lock de escritura pendiente de esa misma sentencia) se salta silenciosamente
/// (<c>READPAST</c>) en vez de esperar a que se libere, así que dos instancias que sondean al mismo
/// tiempo reclaman conjuntos disjuntos de filas sin bloquearse entre sí. El campo
/// <see cref="OutboxMessage.LockedUntilUtc"/> es la segunda mitad del mecanismo: una vez que la fila
/// sale de esa sentencia (auto-commit, sentencia única), el lock de fila de SQL Server ya no la
/// protege — <see cref="OutboxMessage.LockedUntilUtc"/> es lo que impide que otra instancia la reclame
/// mientras esta sigue viva procesándola (o hasta <see cref="OutboxPublisherOptions.LockDuration"/>
/// después, si el proceso murió sin llegar a liberarlo explícitamente).
/// </para>
/// <para>
/// <b>Mapeo <see cref="OutboxMessage"/> → <see cref="IIntegrationEvent"/> (la decisión de diseño más
/// delicada de esta tarea):</b> <see cref="OutboxMessage.EventType"/> es el
/// <c>Type.AssemblyQualifiedName</c> del <c>DomainEvent</c> interno (F1-23) — NO necesariamente el de
/// un <see cref="IIntegrationEvent"/>. Esta clase resuelve ese tipo con <see cref="Type.GetType(string, bool)"/>
/// y deserializa <see cref="OutboxMessage.PayloadJson"/> a esa instancia concreta. Si el resultado
/// implementa <see cref="IIntegrationEvent"/> (porque su autor hizo que el <c>record</c> del
/// <c>DomainEvent</c> implementara también esa interfaz — ver <c>docs/guia-outbox-publisher.md</c>), se
/// publica tal cual. Si NO la implementa, es un <c>DomainEvent</c> puramente interno que nunca fue
/// pensado para cruzar el límite del bounded context: la fila se marca como procesada de todos modos
/// (ya fue "considerada" por el relay), pero nunca se publica nada a Kafka. Esto evita tener que
/// mantener una tabla de mapeo/registro adicional por bounded context: la decisión de qué eventos son
/// de integración queda en el propio tipo del evento, igual que <c>IHasDomainEvents</c>/
/// <c>AggregateRoot&lt;TId&gt;</c> ya deciden qué agregados participan del Outbox (F1-23).
/// </para>
/// <para>
/// <b>Fallos de publicación y reintentos (F3-07):</b> si <see cref="IEventPublisher.PublishAsync(IIntegrationEvent, System.Threading.CancellationToken)"/>
/// lanza una excepción para una fila del lote, esa fila NO se marca como procesada (queda con
/// <see cref="OutboxMessage.RetryCount"/> incrementado y <see cref="OutboxMessage.Error"/> con el
/// mensaje) — las demás filas del lote siguen procesándose con normalidad (mismo criterio documentado
/// en la firma de <c>IEventPublisher.PublishAsync(IEnumerable&lt;IIntegrationEvent&gt;, ...)</c>: sin
/// atomicidad entre eventos del lote frente al broker). A diferencia de F3-03 (donde el lock se liberaba
/// para reintento inmediato en el siguiente ciclo, sin límite), F3-07 clasifica cada excepción con
/// <see cref="IEventPublishFailureClassifier"/> y decide entre dos caminos:
/// <list type="bullet">
/// <item>
/// <b>Transitorio, dentro del límite</b> (<see cref="EventRetryPolicyOptions.MaxAttempts"/> de
/// <see cref="OutboxPublisherOptions.Retry"/>): se calcula un backoff exponencial con jitter
/// (<see cref="EventRetryBackoff.CalculateDelay"/>) a partir de <see cref="OutboxMessage.RetryCount"/>
/// y se fija <see cref="OutboxMessage.LockedUntilUtc"/> a ese momento futuro — reutilizando el mismo
/// campo que ya protege contra reclamos concurrentes entre réplicas (F3-03), ahora también como "no
/// reclamar antes de" para el backoff de reintento, no solo como lock temporal de "en proceso".
/// </item>
/// <item>
/// <b>Permanente, o transitorio que ya agotó el límite:</b> se fija <see cref="OutboxMessage.ExhaustedAtUtc"/>
/// — la fila deja de ser candidata en <see cref="ClaimBatchAsync"/> (nuevo filtro <c>ExhaustedAtUtc IS
/// NULL</c>) para siempre, pero NUNCA se marca <see cref="OutboxMessage.ProcessedAtUtc"/> ni se borra:
/// sigue existiendo, consultable, como punto de extensión explícito para F3-08 (DLQ)/intervención
/// manual — perderla silenciosamente violaría "reinicio no pierde eventos" (F3-03).
/// </item>
/// </list>
/// </para>
/// <para>
/// <b>DLQ (F3-08):</b> inmediatamente después de persistir <see cref="OutboxMessage.ExhaustedAtUtc"/>
/// (nunca antes), se publica además una copia best-effort a un tópico dead-letter real vía
/// <see cref="IDeadLetterPublisher"/> (ver <see cref="PublishDeadLetterIfJustExhaustedAsync"/>) — un
/// operador que solo mira Kafka puede ver el mensaje agotado sin consultar la base de datos. Esto se
/// implementó DENTRO de este relay (en vez de un componente separado que reaccione por polling a filas
/// con <c>ExhaustedAtUtc</c>) porque el propio ciclo de <see cref="ProcessBatchAsync"/> ya identifica el
/// momento exacto del agotamiento sin ningún sondeo adicional — agregar un segundo poller solo para
/// republicar a DLQ hubiera duplicado el mecanismo de reclamo/lock de <see cref="ClaimBatchAsync"/> sin
/// ganar nada, y el reprocesamiento (reset de <c>ExhaustedAtUtc</c> + auditoría) es una operación
/// deliberadamente separada, disparada por un operador — no automática — que si vive en
/// <c>Shared.Infrastructure.Security</c> (<c>IDeadLetterReprocessor</c>, ver
/// <c>docs/runbook-dlq.md</c>) porque necesita <c>IAuditWriter</c> (Épica F2-D), al que este proyecto
/// (<c>Shared.Infrastructure.Persistence</c>) no puede referenciar sin introducir una dependencia
/// circular (<c>Shared.Infrastructure.Security</c> ya referencia este proyecto, no al revés).
/// </para>
/// <para>
/// <b>Reinicio no pierde eventos (criterio de aceptación literal de F3-03):</b> el <c>SaveChangesAsync</c>
/// que marca <see cref="OutboxMessage.ProcessedAtUtc"/> se ejecuta INMEDIATAMENTE después de que la
/// publicación de ESA fila tuvo éxito (no al final de todo el lote) — minimiza, sin eliminarlo del
/// todo, el intervalo en el que un evento ya fue entregado al broker pero la fila todavía no quedó
/// marcada. Si el proceso muere exactamente en ese intervalo, el reinicio (con el lock ya expirado o
/// liberado) vuelve a publicar la misma fila: el evento llega dos veces al broker (duplicado
/// aceptable, semántica "at-least-once" ya documentada en el Plan Maestro, sección 3.2 — nunca se
/// promete exactly-once de punta a punta), pero la fila nunca queda huérfana sin publicar.
/// </para>
/// </remarks>
public sealed class OutboxBatchProcessor(
    DbContext context,
    IEventPublisher eventPublisher,
    OutboxPublisherOptions options,
    IEventPublishFailureClassifier failureClassifier,
    ILogger<OutboxBatchProcessor> logger,
    IDeadLetterPublisher? deadLetterPublisher = null)
{
    public async Task<OutboxBatchResult> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var claimed = await ClaimBatchAsync(now, cancellationToken).ConfigureAwait(false);

        if (claimed.Count == 0)
        {
            return OutboxBatchResult.Empty;
        }

        var published = 0;
        var skippedInternal = 0;
        var failed = 0;
        var exhausted = 0;

        foreach (var message in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            object? deserialized;
            Type? eventType;
            try
            {
                eventType = Type.GetType(message.EventType, throwOnError: false);
                deserialized = eventType is null ? null : JsonSerializer.Deserialize(message.PayloadJson, eventType);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "No se pudo deserializar OutboxMessage {OutboxMessageId} (EventType {EventType}); se reintentará en el próximo ciclo.",
                    message.Id,
                    message.EventType);
                eventType = null;
                deserialized = null;
            }

            if (eventType is null || deserialized is null)
            {
                // Tipo irresolvible/payload no deserializable: F3-07 lo trata siempre como error
                // PERMANENTE — ningún reintento futuro puede cambiar el hecho de que el tipo no existe
                // o el JSON no coincide con él (a diferencia de un fallo de publicación contra el
                // broker, que sí puede depender de una condición transitoria externa).
                RegisterFailure(
                    message,
                    $"No se pudo resolver/deserializar el tipo '{message.EventType}'.",
                    EventPublishFailureKind.Permanent,
                    now);
                failed++;
                if (message.ExhaustedAtUtc is not null)
                {
                    exhausted++;
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await PublishDeadLetterIfJustExhaustedAsync(message, message.EventType, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (deserialized is not IIntegrationEvent integrationEvent)
            {
                // DomainEvent puramente interno: nunca implementó IIntegrationEvent a propósito, así
                // que no cruza el límite del bounded context. Ver docs/guia-outbox-publisher.md.
                message.ProcessedAtUtc = DateTime.UtcNow;
                message.LockedUntilUtc = null;
                message.LockedBy = null;
                skippedInternal++;
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await eventPublisher.PublishAsync(integrationEvent, cancellationToken).ConfigureAwait(false);

                message.ProcessedAtUtc = DateTime.UtcNow;
                message.LockedUntilUtc = null;
                message.LockedBy = null;
                message.Error = null;
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var failureKind = failureClassifier.Classify(ex);
                RegisterFailure(message, ex.Message, failureKind, now);
                failed++;
                if (message.ExhaustedAtUtc is not null)
                {
                    exhausted++;
                }

                logger.LogWarning(
                    ex,
                    "Fallo ({FailureKind}) al publicar OutboxMessage {OutboxMessageId} (EventType {EventType}, intento {RetryCount}); {Outcome}.",
                    failureKind,
                    message.Id,
                    integrationEvent.EventType,
                    message.RetryCount,
                    message.ExhaustedAtUtc is null
                        ? $"se reintentará no antes de {message.LockedUntilUtc:O}"
                        : "se marcó como agotado, ya no se reintentará");
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await PublishDeadLetterIfJustExhaustedAsync(message, integrationEvent.EventType, cancellationToken).ConfigureAwait(false);
        }

        return new OutboxBatchResult(claimed.Count, published, skippedInternal, failed, exhausted);
    }

    /// <summary>
    /// F3-07: centraliza qué le pasa a una fila que falló este ciclo — incrementa
    /// <see cref="OutboxMessage.RetryCount"/>, registra el motivo, y decide entre agotarla
    /// (<see cref="OutboxMessage.ExhaustedAtUtc"/>, ya sea porque <paramref name="failureKind"/>
    /// es <see cref="EventPublishFailureKind.Permanent"/> o porque superó
    /// <see cref="EventRetryPolicyOptions.MaxAttempts"/>) o programar el próximo intento con backoff
    /// exponencial y jitter (<see cref="OutboxMessage.LockedUntilUtc"/> — reutilizado como
    /// "no reclamar antes de", no solo como lock entre réplicas, ver remarks de la clase).
    /// </summary>
    private void RegisterFailure(OutboxMessage message, string error, EventPublishFailureKind failureKind, DateTime now)
    {
        message.RetryCount++;
        message.Error = error;
        message.LockedBy = null;

        var isPermanent = failureKind == EventPublishFailureKind.Permanent;
        var isExhausted = isPermanent || EventRetryBackoff.IsExhausted(message.RetryCount, options.Retry);

        if (isExhausted)
        {
            message.ExhaustedAtUtc = now;
            // Una fila agotada nunca vuelve a cumplir "LockedUntilUtc < now" en el futuro por diseño
            // (ver el WHERE de ClaimBatchAsync, que además exige ExhaustedAtUtc IS NULL): fijar
            // LockedUntilUtc en null acá es solo higiene de datos, no participa de si se reclama de
            // nuevo — ExhaustedAtUtc es lo que la excluye.
            message.LockedUntilUtc = null;
            return;
        }

        var delay = EventRetryBackoff.CalculateDelay(message.RetryCount, options.Retry);
        message.LockedUntilUtc = now.Add(delay);
    }

    /// <summary>
    /// F3-08 (DLQ): si <paramref name="message"/> quedó marcada agotada EN ESTA MISMA iteración (ver los
    /// dos llamadores, ambos inmediatamente después del <c>SaveChangesAsync</c> que persistió
    /// <see cref="OutboxMessage.ExhaustedAtUtc"/>), publica una copia a un tópico dead-letter real
    /// (<see cref="IDeadLetterPublisher"/>, best-effort). Nunca se invoca ANTES de persistir el
    /// agotamiento: <see cref="OutboxMessage"/> — no Kafka — sigue siendo la fuente de verdad de que un
    /// mensaje se agotó (F3-03, "reinicio no pierde eventos"); si el proceso muere entre el
    /// <c>SaveChangesAsync</c> y esta llamada, la fila queda igualmente agotada y consultable, solo sin
    /// la copia de conveniencia en Kafka — un operador siempre puede consultar
    /// <c>WHERE ExhaustedAtUtc IS NOT NULL</c> aunque la publicación a DLQ nunca haya llegado a ocurrir.
    /// Si <see cref="IDeadLetterPublisher"/> no está registrado (ningún adapter de broker configurado) o
    /// la publicación en sí falla, se registra un warning y el ciclo continúa sin abortar el resto del
    /// lote — un fallo de "notificación de conveniencia" nunca debe impedir que seiga procesándose el
    /// resto de las filas reclamadas.
    /// </summary>
    private async Task PublishDeadLetterIfJustExhaustedAsync(OutboxMessage message, string eventTypeForDlq, CancellationToken cancellationToken)
    {
        if (message.ExhaustedAtUtc is null || deadLetterPublisher is null)
        {
            return;
        }

        try
        {
            await deadLetterPublisher.PublishAsync(
                new DeadLetterEnvelope
                {
                    EventType = eventTypeForDlq,
                    Payload = Encoding.UTF8.GetBytes(message.PayloadJson),
                    Reason = message.Error ?? "Motivo no disponible.",
                    Attempts = message.RetryCount,
                    ExhaustedAtUtc = message.ExhaustedAtUtc.Value,
                    SourceMessageId = message.Id.ToString(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "No se pudo publicar OutboxMessage {OutboxMessageId} al tópico dead-letter; la fila " +
                "ya quedó agotada (ExhaustedAtUtc persistido) y sigue consultable/reprocesable manualmente.",
                message.Id);
        }
    }

    /// <summary>
    /// Reclama hasta <see cref="OutboxPublisherOptions.BatchSize"/> filas pendientes en una única
    /// sentencia SQL (CTE + <c>UPDATE ... OUTPUT</c>) con <c>UPDLOCK, ROWLOCK, READPAST</c> — ver el
    /// comentario de bloqueo en el <c>remarks</c> de la clase. <c>IgnoreQueryFilters()</c> es
    /// necesario porque este relay procesa TODOS los tenants, no el tenant del scope actual (que aquí
    /// ni siquiera existe: no hay ningún <c>ITenantProvider</c> de request de por medio).
    /// </summary>
    private async Task<List<OutboxMessage>> ClaimBatchAsync(DateTime now, CancellationToken cancellationToken)
    {
        var entityType = context.Model.FindEntityType(typeof(OutboxMessage))
            ?? throw new InvalidOperationException(
                "OutboxMessage no está registrado en el modelo de EF Core: falta invocar " +
                "OutboxModelConfigurator.Configure(modelBuilder) en el DbContext consumidor.");

        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException("OutboxMessage no tiene una tabla asignada en el modelo de EF Core.");
        var schema = entityType.GetSchema();
        var qualifiedTable = schema is null ? $"[{tableName}]" : $"[{schema}].[{tableName}]";
        var lockUntil = now.Add(options.LockDuration);

        // Con el prefijo `$$` (raw string literal interpolado "de segundo nivel"), solo `{{expr}}`
        // interpola de verdad — un `{N}` simple queda literal en el SQL resultante (placeholders
        // posicionales de FromSqlRaw). El nombre de tabla/esquema viene de los metadatos de EF Core
        // (confiable, no input de usuario), así que interpolarlo directamente en el texto del comando
        // no es una inyección SQL.
        var sql = $$"""
            WITH candidates AS (
                SELECT TOP ({0}) *
                FROM {{qualifiedTable}} WITH (UPDLOCK, ROWLOCK, READPAST)
                WHERE [ProcessedAtUtc] IS NULL
                  AND [ExhaustedAtUtc] IS NULL
                  AND ([LockedUntilUtc] IS NULL OR [LockedUntilUtc] < {1})
                ORDER BY [OccurredAtUtc] ASC, [Id] ASC
            )
            UPDATE candidates
            SET [LockedUntilUtc] = {2}, [LockedBy] = {3}
            OUTPUT inserted.*;
            """;

        return await context.Set<OutboxMessage>()
            .FromSqlRaw(sql, options.BatchSize, now, lockUntil, options.WorkerId)
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
