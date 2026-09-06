using System.Collections.Concurrent;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Implementación por defecto de <see cref="IAuditWriter"/> (F2-15, Épica F2-D): un almacenamiento en
/// memoria del propio proceso, sin persistencia entre reinicios ni entre instancias -- placeholder
/// registrado por <see cref="AuditServiceCollectionExtensions.AddSharedAuditing"/> para que la interfaz
/// quede lista y probada, no una fuente de verdad productiva de auditoría (una auditoría que se pierde al
/// reiniciar el proceso no cumple ningún objetivo de trazabilidad real). Un proyecto consumidor que
/// necesite auditoría persistente conecta su propia implementación (tabla SQL append-only, event store, o
/// el destino WORM de F2-18) registrándola después de <c>AddSharedAuditing</c>.
/// </summary>
public sealed class InMemoryAuditWriter : IAuditWriter
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public Task<Result<AuditEntry>> WriteAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var id = Guid.NewGuid();
        var occurredAtUtc = DateTime.UtcNow;
        var auditHash = AuditHashCalculator.Compute(
            id, occurredAtUtc, request.Actor, request.TenantId, request.Action, request.Resource,
            request.Outcome, request.Reason, request.CorrelationId, request.TraceId, request.IpAddress,
            request.Metadata);

        var entry = new AuditEntry(
            id, occurredAtUtc, request.Actor, request.TenantId, request.Action, request.Resource,
            request.Outcome, request.Reason, request.CorrelationId, request.TraceId, request.IpAddress,
            request.Metadata, auditHash);

        // Append-only: la única operación sobre _entries en todo este tipo es Enqueue -- no existe ningún
        // camino de código (acá ni en la interfaz IAuditWriter) que remueva o reemplace un elemento ya
        // agregado.
        _entries.Enqueue(entry);

        return Task.FromResult(Result.Success(entry));
    }

    /// <summary>
    /// Copia de solo lectura de las entradas escritas hasta el momento -- para inspección en pruebas o en
    /// un proyecto que use este writer también como lectura simple durante desarrollo local. Devuelve un
    /// array nuevo en cada llamada (nunca la colección interna): mutar el array devuelto no afecta el
    /// estado de este writer, y no hay ninguna otra forma de alterar una entrada ya agregada.
    /// </summary>
    public IReadOnlyList<AuditEntry> Entries => _entries.ToArray();
}
