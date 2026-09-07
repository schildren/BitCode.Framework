using System.Collections.Concurrent;
using BitCode.Framework.Shared.Infrastructure.Security.Audit.Query;
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
/// <remarks>
/// F2-16 (cadena de integridad): mantiene el último <see cref="AuditEntry.AuditHash"/> escrito por cadena
/// (una cadena por <see cref="AuditEntry.TenantId"/>, incluido <see langword="null"/> para operaciones de
/// plataforma sin tenant -- ver <see cref="AuditEntry.PreviousAuditHash"/>). La clave que identifica una
/// cadena es <see cref="ChainKey"/>, no <c>Guid?</c> directamente: un diccionario concurrente no admite
/// realmente una clave <see langword="null"/> en tiempo de ejecución (un <c>Guid?</c> sin valor se boxea a
/// una referencia nula real) -- <see cref="ChainKey"/> es una <see langword="struct"/> que nunca es
/// <see langword="null"/> en tiempo de ejecución, sea cual sea el <c>TenantId</c> que representa.
/// <para>
/// Determinar el <c>previousAuditHash</c> de una entrada nueva (leer el último hash de la cadena) y hacer
/// visible esa misma entrada en <see cref="Entries"/> (<c>Enqueue</c>) NO son operaciones independientes:
/// deben ejecutarse como una única sección atómica por cadena, porque <see cref="Entries"/> se recorre en
/// orden de aparición y ese orden tiene que coincidir siempre con el orden lógico del enlace
/// <see cref="AuditEntry.PreviousAuditHash"/> que <see cref="IAuditIntegrityVerifier.Verify"/> valida. Si
/// se hicieran por separado (como en una versión anterior de este tipo, que actualizaba el "último hash"
/// con <see cref="ConcurrentDictionary{TKey,TValue}.AddOrUpdate(TKey, System.Func{TKey,TValue},
/// System.Func{TKey,TValue,TValue})"/> y recién después encolaba), dos escrituras concurrentes para el
/// mismo tenant podían completar esas dos operaciones en órdenes relativos distintos entre sí (T1 fija el
/// enlace antes que T2, pero T2 encola antes que T1) -- el resultado es una entrada cuyo
/// <c>PreviousAuditHash</c> no coincide con el hash de la entrada que la antecede en <see cref="Entries"/>,
/// que <see cref="IAuditIntegrityVerifier.Verify"/> reporta como <see
/// cref="AuditIntegrityBreakReason.PreviousHashLinkMismatch"/>: un falso positivo de manipulación de la
/// cadena causado únicamente por esta condición de carrera de implementación, no por ningún dato alterado.
/// Para evitarlo, este tipo usa un <see langword="lock"/> por cadena (<see cref="_chainLocks"/>) que
/// envuelve tanto la lectura/actualización del último hash como el <c>Enqueue</c> de esa misma cadena;
/// cadenas de tenants distintos usan objetos de lock distintos y no se bloquean entre sí.
/// </para>
/// </remarks>
/// <remarks>
/// F2-20 (consulta de auditoría): además implementa <see cref="IAuditReader"/> -- la lectura filtrada y
/// paginada opera sobre la MISMA <see cref="_entries"/> que <see cref="WriteAsync"/> encola, así que un
/// registro es buscable inmediatamente después de escribirse, sin ninguna latencia de propagación
/// adicional (una propiedad del placeholder en memoria que un backend productivo real no necesariamente
/// replica).
/// </remarks>
public sealed class InMemoryAuditWriter : IAuditWriter, IAuditReader
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    // Último AuditHash escrito por cadena (F2-16) -- una entrada de diccionario por TenantId distinto,
    // incluida la cadena de operaciones de plataforma sin tenant (ChainKey.ForTenant(null)). El acceso a
    // esta cadena en particular siempre ocurre bajo el lock de _chainLocks correspondiente (ver WriteAsync)
    // -- ConcurrentDictionary se usa acá solo porque distintas cadenas (distintos locks) pueden tocar el
    // diccionario al mismo tiempo, no porque se dependa de su atomicidad interna para esta invariante.
    private readonly ConcurrentDictionary<ChainKey, string> _lastHashByChain = new();

    // Un objeto de lock por cadena (F2-16): serializa, para una misma cadena, la sección
    // "leer último hash -> construir la entrada -> encolarla" para que el orden de aparición en _entries
    // sea siempre consistente con el orden lógico del enlace PreviousAuditHash. Cadenas distintas
    // (TenantId distinto) usan objetos de lock distintos y nunca se bloquean entre sí.
    private readonly ConcurrentDictionary<ChainKey, object> _chainLocks = new();

    private readonly record struct ChainKey(bool HasTenant, Guid TenantId)
    {
        public static ChainKey ForTenant(Guid? tenantId) =>
            tenantId.HasValue ? new ChainKey(true, tenantId.Value) : new ChainKey(false, default);
    }

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

        var chainKey = ChainKey.ForTenant(request.TenantId);
        var chainLock = _chainLocks.GetOrAdd(chainKey, static _ => new object());

        AuditEntry entry;
        lock (chainLock)
        {
            // F2-16: encadena esta entrada con la última ya escrita para el mismo tenant (o `null` para
            // el primer registro de esa cadena -- el "génesis"). Leer el último hash, construir la entrada
            // y encolarla ocurren dentro del mismo lock de cadena para que el orden de aparición en
            // _entries coincida siempre con el orden lógico de este enlace (ver comentario de <remarks>
            // de este tipo).
            _lastHashByChain.TryGetValue(chainKey, out var previousAuditHash);

            entry = new AuditEntry(
                id, occurredAtUtc, request.Actor, request.TenantId, request.Action, request.Resource,
                request.Outcome, request.Reason, request.CorrelationId, request.TraceId, request.IpAddress,
                request.Metadata, auditHash, previousAuditHash);

            _lastHashByChain[chainKey] = auditHash;

            // Append-only: la única operación sobre _entries en todo este tipo es Enqueue -- no existe
            // ningún camino de código (acá ni en la interfaz IAuditWriter) que remueva o reemplace un
            // elemento ya agregado.
            _entries.Enqueue(entry);
        }

        return Task.FromResult(Result.Success(entry));
    }

    /// <summary>
    /// Copia de solo lectura de las entradas escritas hasta el momento -- para inspección en pruebas o en
    /// un proyecto que use este writer también como lectura simple durante desarrollo local. Devuelve un
    /// array nuevo en cada llamada (nunca la colección interna): mutar el array devuelto no afecta el
    /// estado de este writer, y no hay ninguna otra forma de alterar una entrada ya agregada.
    /// </summary>
    public IReadOnlyList<AuditEntry> Entries => _entries.ToArray();

    /// <summary>
    /// Implementación de <see cref="IAuditReader"/> (F2-20): filtra <see cref="Entries"/> por los criterios
    /// de <paramref name="filter"/> (todos combinables con AND), ordena por <see
    /// cref="AuditEntry.OccurredAtUtc"/> descendente (más reciente primero, el orden esperado de una
    /// búsqueda administrativa) y pagina el resultado con <see cref="AuditSearchFilter.Page"/> -- nunca
    /// devuelve el conjunto completo sin paginar, sea cual sea el filtro.
    /// </summary>
    public Task<Result<PagedResult<AuditEntry>>> SearchAsync(AuditSearchFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = _entries
            .Where(e => filter.FromUtc is null || e.OccurredAtUtc >= filter.FromUtc)
            .Where(e => filter.ToUtc is null || e.OccurredAtUtc <= filter.ToUtc)
            .Where(e => filter.TenantId is null || e.TenantId == filter.TenantId)
            .Where(e => filter.ActorId is null || string.Equals(e.Actor.Id, filter.ActorId, StringComparison.Ordinal))
            .Where(e => filter.Action is null || string.Equals(e.Action, filter.Action, StringComparison.Ordinal))
            .Where(e => filter.Outcome is null || e.Outcome == filter.Outcome)
            .Where(e => filter.ResourceType is null || string.Equals(e.Resource.Type, filter.ResourceType, StringComparison.Ordinal))
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToArray();

        var totalCount = matches.Length;
        var items = matches
            .Skip((filter.Page.Page - 1) * filter.Page.PageSize)
            .Take(filter.Page.PageSize)
            .ToArray();

        return Task.FromResult(Result.Success(new PagedResult<AuditEntry>(items, filter.Page.Page, filter.Page.PageSize, totalCount)));
    }
}
