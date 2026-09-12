using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.FeatureFlags;

/// <summary>
/// Audita cada transición de valor (old -> new) de un feature flag externalizado (F4-12) usando la
/// infraestructura de auditoría inmutable ya construida en Fase 2 (<see cref="IAuditWriter"/>, F2-15 a
/// F2-20 -- cadena de hash, firma HMAC de lotes, exportación WORM), en vez de un mecanismo de log nuevo.
/// <para>
/// Limitación deliberada, documentada explícitamente (no un defecto pendiente de corregir): cuando el
/// flag se externaliza vía un <c>ConfigMap</c> con <c>reloadOnChange: true</c> (patrón F4-02 en adelante),
/// el cambio ocurre FUERA del proceso .NET (<c>kubectl apply</c>, un pipeline de despliegue, un operador de
/// GitOps) -- este servicio detecta la transición del VALOR (qué flag cambió, de qué a qué, cuándo) pero no
/// conoce QUIÉN la originó, porque esa identidad nunca llega al proceso. <see cref="AuditActor"/> se registra
/// siempre como <see cref="AuditActorType.System"/> con un id fijo (<see cref="SystemActorId"/>) por ese
/// motivo. Un flujo con auditoría de actor humano identificado requeriría un endpoint administrativo propio
/// que reciba el cambio autenticado -- eso es superficie del módulo "Feature Management" reservado para la
/// Fase 6 (Plan Maestro, tabla de módulos, orden 4: "Flags, segmentos, rollout y auditoría"), fuera de
/// alcance de esta tarea a propósito.
/// </para>
/// </summary>
public sealed class FeatureFlagChangeAuditingService : IHostedService, IDisposable
{
    /// <summary>
    /// Identificador fijo del actor de sistema usado en cada <see cref="AuditEntry"/> emitida por este
    /// servicio (ver la limitación documentada en el resumen del tipo: no hay actor humano disponible).
    /// </summary>
    public const string SystemActorId = "system.featureflags.config-reload";

    /// <summary>Acción registrada en cada <see cref="AuditEntry"/> (misma convención "{entidad}.{accion}" que un permiso RBAC).</summary>
    public const string AuditAction = "featureflags.changed";

    /// <summary><see cref="AuditResource.Type"/> usado por cada entrada emitida por este servicio.</summary>
    public const string AuditResourceType = "feature-flag";

    private readonly IOptionsMonitor<FeatureFlagsOptions> _optionsMonitor;
    private readonly IAuditWriter _auditWriter;
    private readonly ILogger<FeatureFlagChangeAuditingService> _logger;

    private readonly object _snapshotLock = new();
    private IReadOnlyDictionary<string, bool> _lastKnownFlags;
    private IDisposable? _changeRegistration;

    public FeatureFlagChangeAuditingService(
        IOptionsMonitor<FeatureFlagsOptions> optionsMonitor,
        IAuditWriter auditWriter,
        ILogger<FeatureFlagChangeAuditingService> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(logger);

        _optionsMonitor = optionsMonitor;
        _auditWriter = auditWriter;
        _logger = logger;
        _lastKnownFlags = Snapshot(optionsMonitor.CurrentValue);
    }

    /// <summary>
    /// Se suscribe a <see cref="IOptionsMonitor{TOptions}.OnChange"/> -- la misma señal de recarga que
    /// dispara cualquier proveedor de configuración con soporte de change token (archivo con
    /// <c>reloadOnChange: true</c>, variables de entorno reevaluadas explícitamente, etc.), sin acoplarse a
    /// un mecanismo concreto de Kubernetes.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _changeRegistration = _optionsMonitor.OnChange(OnFeatureFlagsChanged);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _changeRegistration?.Dispose();
        _changeRegistration = null;
        return Task.CompletedTask;
    }

    public void Dispose() => _changeRegistration?.Dispose();

    private void OnFeatureFlagsChanged(FeatureFlagsOptions options)
    {
        IReadOnlyDictionary<string, bool> previous;
        IReadOnlyDictionary<string, bool> current;

        // Sección crítica corta: solo calcula el diff y actualiza el snapshot conocido. La escritura de
        // auditoría en sí (I/O) queda deliberadamente fuera del lock -- no hace falta serializar auditorías
        // de flags distintos entre sí, y mantener I/O dentro de un lock arriesga bloquear la siguiente
        // recarga si IAuditWriter.WriteAsync tarda.
        lock (_snapshotLock)
        {
            previous = _lastKnownFlags;
            current = Snapshot(options);
            _lastKnownFlags = current;
        }

        foreach (var change in ComputeChanges(previous, current))
        {
            // Fire-and-forget deliberado: OnChange de IOptionsMonitor es un callback síncrono (Action<T>),
            // sin forma de propagar un Task al framework de opciones. Un fallo al auditar (IAuditWriter
            // caído/Result.Failure, o una excepción inesperada) nunca debe impedir que el flag recargado
            // surta efecto para IFeatureFlagProvider.IsEnabled -- coherente con el resto del framework
            // (por ejemplo AuditingAuthorizationPolicyEvaluator: un fallo de auditoría no bloquea la
            // operación auditada). El fallo, si ocurre, queda igual visible en logs estructurados.
            _ = AuditChangeAsync(change);
        }
    }

    private async Task AuditChangeAsync(FeatureFlagChange change)
    {
        try
        {
            var request = new AuditEntryRequest(
                actor: new AuditActor(SystemActorId, AuditActorType.System),
                tenantId: null,
                action: AuditAction,
                resource: new AuditResource(AuditResourceType, change.FlagName),
                outcome: AuditOutcome.Success,
                reason: "Recarga de configuración externa (ConfigMap/appsettings), sin actor humano identificable -- ver docs/politica-configuracion-y-feature-flags.md.",
                metadata: new Dictionary<string, string?>
                {
                    ["oldValue"] = change.OldValue,
                    ["newValue"] = change.NewValue,
                });

            var result = await _auditWriter.WriteAsync(request).ConfigureAwait(false);
            if (result.IsFailure)
            {
                _logger.LogWarning(
                    "No se pudo auditar el cambio del feature flag {FlagName} ({OldValue} -> {NewValue}): {ErrorCode}",
                    change.FlagName,
                    change.OldValue,
                    change.NewValue,
                    result.Error.Code);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Excepción al auditar el cambio del feature flag {FlagName} ({OldValue} -> {NewValue})",
                change.FlagName,
                change.OldValue,
                change.NewValue);
        }
    }

    /// <summary>
    /// Calcula las transiciones old -> new entre dos snapshots: valores agregados, removidos (vuelven al
    /// <c>defaultValue</c> del llamador de <see cref="IFeatureFlagProvider.IsEnabled"/>, representado acá
    /// como <c>newValue = null</c>) y modificados. Un flag presente en ambos snapshots con el mismo valor
    /// no genera ningún cambio -- <see cref="IOptionsMonitor{TOptions}.OnChange"/> puede dispararse por una
    /// recarga de configuración que no tocó ninguna clave bajo <see cref="FeatureFlagsOptions.SectionName"/>.
    /// </summary>
    internal static IReadOnlyList<FeatureFlagChange> ComputeChanges(
        IReadOnlyDictionary<string, bool> previous,
        IReadOnlyDictionary<string, bool> current)
    {
        var changes = new List<FeatureFlagChange>();

        foreach (var (flagName, newValue) in current)
        {
            if (!previous.TryGetValue(flagName, out var oldValue))
            {
                changes.Add(new FeatureFlagChange(flagName, null, newValue.ToString()));
            }
            else if (oldValue != newValue)
            {
                changes.Add(new FeatureFlagChange(flagName, oldValue.ToString(), newValue.ToString()));
            }
        }

        foreach (var (flagName, oldValue) in previous)
        {
            if (!current.ContainsKey(flagName))
            {
                changes.Add(new FeatureFlagChange(flagName, oldValue.ToString(), null));
            }
        }

        return changes;
    }

    private static IReadOnlyDictionary<string, bool> Snapshot(FeatureFlagsOptions options) =>
        new Dictionary<string, bool>(options, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Una transición de valor detectada de un feature flag (F4-12) -- ver <see cref="FeatureFlagChangeAuditingService.ComputeChanges"/>.</summary>
internal sealed record FeatureFlagChange(string FlagName, string? OldValue, string? NewValue);
