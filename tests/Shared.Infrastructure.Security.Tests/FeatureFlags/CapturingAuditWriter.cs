using System.Collections.Concurrent;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.FeatureFlags;

/// <summary>
/// Captura cada <see cref="AuditEntryRequest"/> escrito, exponiendo un mecanismo de espera asíncrono (en
/// vez de <c>Task.Delay</c> fijo) para el escenario "fire-and-forget" de
/// <see cref="FeatureFlagChangeAuditingService"/> (el callback <c>OnChange</c> de
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> es síncrono, así que la escritura
/// de auditoría corre en un <see cref="Task"/> desatendido). Extraído a un archivo propio (antes vivía
/// solo dentro de <c>FeatureFlagChangeAuditingServiceTests</c>) para que
/// <c>FeatureFlagsFileHotReloadIntegrationTests</c> (F4-12, cierre del gap de hot-reload) lo reutilice sin
/// duplicar la implementación.
/// </summary>
internal sealed class CapturingAuditWriter : IAuditWriter
{
    private readonly ConcurrentQueue<AuditEntryRequest> _pending = new();
    private TaskCompletionSource<AuditEntryRequest>? _waiter;

    public ConcurrentQueue<AuditEntryRequest> WrittenRequests { get; } = new();

    public Task<Result<AuditEntry>> WriteAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
    {
        WrittenRequests.Enqueue(request);

        var waiter = Interlocked.Exchange(ref _waiter, null);
        if (waiter is not null)
        {
            waiter.TrySetResult(request);
        }
        else
        {
            _pending.Enqueue(request);
        }

        var entry = new AuditEntry(
            Guid.NewGuid(),
            DateTime.UtcNow,
            request.Actor,
            request.TenantId,
            request.Action,
            request.Resource,
            request.Outcome,
            request.Reason,
            request.CorrelationId,
            request.TraceId,
            request.IpAddress,
            request.Metadata,
            auditHash: "TEST-HASH");

        return Task.FromResult(Result<AuditEntry>.Success(entry));
    }

    public async Task<AuditEntryRequest> WaitForNextAsync(TimeSpan? timeout = null)
    {
        if (_pending.TryDequeue(out var already))
        {
            return already;
        }

        var tcs = new TaskCompletionSource<AuditEntryRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _waiter, tcs);

        // Chequeo de última hora por si la escritura llegó entre el TryDequeue y el Exchange de arriba.
        if (_pending.TryDequeue(out var raceWinner))
        {
            tcs.TrySetResult(raceWinner);
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout ?? TimeSpan.FromSeconds(10)));
        if (completed != tcs.Task)
        {
            throw new TimeoutException("No se recibió ninguna escritura de auditoría dentro del tiempo esperado.");
        }

        return await tcs.Task;
    }
}
