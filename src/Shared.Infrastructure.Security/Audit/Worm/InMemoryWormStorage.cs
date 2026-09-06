using System.Collections.Concurrent;
using System.Security.Cryptography;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

/// <summary>
/// Implementación de referencia/desarrollo de <see cref="IWormStorage"/> (F2-18, Épica F2-D): un
/// almacenamiento en memoria del propio proceso, sin persistencia entre reinicios ni entre instancias --
/// EXACTAMENTE el mismo criterio y las mismas limitaciones que <see cref="InMemoryAuditWriter"/> (F2-15):
/// un placeholder que modela correctamente la semántica WORM (rechaza sobrescritura y eliminación
/// prematura, ver <see cref="IWormStorage"/>) y queda probado como tal, pero NO es una fuente de verdad
/// productiva -- un objeto "inmutable" que desaparece al reiniciar el proceso no cumple ningún objetivo
/// real de retención regulatoria. Un proyecto consumidor que necesite WORM real conecta su propia
/// implementación de <see cref="IWormStorage"/> (por ejemplo, sobre MinIO/S3 Object Lock o Azure Blob
/// Storage con Immutable Storage -- ver ADR 0017, `Proposed`) registrándola después de <see
/// cref="AuditWormExportServiceCollectionExtensions.AddSharedAuditWormExport"/>.
/// </summary>
public sealed class InMemoryWormStorage : IWormStorage
{
    private sealed class StoredObject
    {
        public byte[]? Content;
        public required WormObjectMetadata Metadata { get; init; }
    }

    // Una entrada de diccionario permanece para siempre una vez creada (incluso tras "eliminarse" -- el
    // Content se limpia pero la clave sigue ocupada) -- esto es lo que hace cumplir "write-once real, no
    // solo durante la retención" (ver IWormStorage): ninguna clave usada alguna vez puede reutilizarse.
    private readonly ConcurrentDictionary<string, StoredObject> _objects = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryWormStorage(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<Result<WormObjectMetadata>> WriteAsync(WormWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var metadata = new WormObjectMetadata(
            request.Key, nowUtc, nowUtc + request.RetentionPeriod, ComputeContentHash(request.Content));

        var stored = new StoredObject { Content = (byte[])request.Content.Clone(), Metadata = metadata };

        if (!_objects.TryAdd(request.Key, stored))
        {
            return Task.FromResult(Result.Failure<WormObjectMetadata>(Error.Conflict(
                "Worm.ObjectAlreadyExists",
                $"Ya existe un objeto WORM con la clave '{request.Key}' -- write-once: una clave usada alguna vez nunca puede reescribirse, ni siquiera después de eliminarse tras expirar su retención.")));
        }

        return Task.FromResult(Result.Success(metadata));
    }

    public Task<Result<WormObject>> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_objects.TryGetValue(key, out var stored))
        {
            return Task.FromResult(Result.Failure<WormObject>(ObjectNotFoundError(key)));
        }

        byte[]? content;
        lock (stored)
        {
            content = stored.Content is null ? null : (byte[])stored.Content.Clone();
        }

        if (content is null)
        {
            return Task.FromResult(Result.Failure<WormObject>(ObjectNotFoundError(key)));
        }

        return Task.FromResult(Result.Success(new WormObject(stored.Metadata, content)));
    }

    public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_objects.TryGetValue(key, out var stored))
        {
            return Task.FromResult(Result.Failure(ObjectNotFoundError(key)));
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        lock (stored)
        {
            if (stored.Content is null)
            {
                return Task.FromResult(Result.Failure(ObjectNotFoundError(key)));
            }

            if (nowUtc < stored.Metadata.RetentionExpiresAtUtc)
            {
                return Task.FromResult(Result.Failure(Error.Conflict(
                    "Worm.RetentionPeriodNotExpired",
                    $"El objeto WORM '{key}' tiene retención vigente hasta {stored.Metadata.RetentionExpiresAtUtc:O}; no puede eliminarse antes de esa fecha.")));
            }

            // Se limpia el contenido (libera memoria) pero la entrada del diccionario permanece como
            // tombstone permanente -- WriteAsync seguirá rechazando esta misma clave para siempre.
            stored.Content = null;
        }

        return Task.FromResult(Result.Success());
    }

    private static Error ObjectNotFoundError(string key) => Error.NotFound(
        "Worm.ObjectNotFound", $"No existe (o ya fue eliminado) un objeto WORM con la clave '{key}'.");

    private static string ComputeContentHash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
}
