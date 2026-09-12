namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Fuente de la asignación explícita tenant → región propietaria de escritura (F5-02, Fase 5 —
/// Disaster Recovery y multi-región). Se modela como un mapeo explícito y auditable, igual que
/// <see cref="ITenantShardMapStore"/> (F1-13) y por el mismo motivo: agregar una fila de asignación
/// nueva no reubica ningún tenant ya asignado (a diferencia de un hash consistente del
/// <c>tenantId</c> módulo N regiones), y la decisión de negocio "región por tenant" (en vez de
/// "región por agregado/bounded context") es la aplicada aquí — ver el ADR sugerido en
/// <c>docs/mapa-ownership-regional.md</c> para el razonamiento completo. Un mapeo por agregado/bounded
/// context individual, si en el futuro un consumidor lo necesitara, se modelaría como un contrato
/// adicional (no reemplaza a este) resuelto de forma más granular que el tenant.
/// </summary>
public interface ITenantRegionMapStore
{
    /// <summary>
    /// Devuelve el <see cref="RegionId"/> asignado explícitamente a <paramref name="tenantId"/>, o
    /// <see langword="null"/> si no tiene una asignación explícita (en cuyo caso el tenant permanece
    /// en <see cref="RegionId.Primary"/>).
    /// </summary>
    Task<RegionId?> TryGetOwnerRegionAsync(Guid tenantId, CancellationToken cancellationToken);
}
