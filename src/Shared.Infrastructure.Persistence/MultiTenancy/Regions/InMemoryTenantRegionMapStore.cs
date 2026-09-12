using System.Collections.Concurrent;
using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Regions;

/// <summary>
/// Implementación de referencia de <see cref="ITenantRegionMapStore"/> para F5-02: mantiene la
/// asignación tenant → región propietaria en un diccionario en memoria del proceso. Mismo patrón (y
/// misma limitación documentada) que <c>InMemoryTenantShardMapStore</c> (F1-13).
/// </summary>
/// <remarks>
/// <b>No apta para producción multi-instancia/multi-región</b>: cada instancia del proceso tendría su
/// propia copia del mapeo, y un reinicio la pierde por completo. Sirve para (a) demostrar y probar de
/// forma determinística el contrato <see cref="ITenantRegionMapStore"/>/
/// <see cref="TenantRegionOwnershipResolver"/> sin depender de infraestructura multi-región real (que
/// hoy no existe en este repositorio — un solo host de desarrollo, ver <c>docs/bia-fase5.md</c>), y
/// (b) como base de referencia rápida para tests o entornos de un solo proceso. La implementación
/// productiva equivalente (una tabla de control replicada de forma síncrona entre regiones, o un
/// servicio de configuración global tipo etcd/Consul multi-región) es una tarea de implementación
/// posterior de infraestructura real, fuera del alcance de F5-02 (que pide "mapa de ownership" y el
/// mecanismo que lo exprese/valide, no la topología de replicación de ese mapa — eso es F5-04).
/// </remarks>
public sealed class InMemoryTenantRegionMapStore : ITenantRegionMapStore
{
    private readonly ConcurrentDictionary<Guid, RegionId> _assignments = new();

    /// <summary>
    /// Asigna explícitamente <paramref name="tenantId"/> a <paramref name="regionId"/> como su región
    /// propietaria de escritura. Idempotente: reasignar el mismo tenant a la misma región no tiene
    /// efecto adicional; reasignarlo a una región distinta sobrescribe la asignación anterior (un
    /// failover/failback regional real de datos es responsabilidad de quien orqueste esa migración —
    /// ver la sección 13 del Plan Maestro, "habilitación de tráfico productivo" y "failover/failback
    /// productivo" requieren aprobación humana explícita, este store solo refleja el mapa vigente).
    /// </summary>
    public void Assign(Guid tenantId, RegionId regionId) => _assignments[tenantId] = regionId;

    public Task<RegionId?> TryGetOwnerRegionAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<RegionId?>(_assignments.TryGetValue(tenantId, out var regionId) ? regionId : null);
}
