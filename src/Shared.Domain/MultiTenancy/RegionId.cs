namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Identificador opaco y determinístico de una región de cómputo (F5-02, Fase 5 — Disaster Recovery y
/// multi-región). Análogo a <see cref="ShardId"/> (F1-13) pero en el eje de topología física/geográfica
/// en vez de en el eje de partición de base de datos: mientras <see cref="ShardId"/> responde "¿en qué
/// base de datos física vive este tenant?", <see cref="RegionId"/> responde "¿qué región es la única
/// propietaria de escritura (single-writer) para este tenant/agregado?", siguiendo la decisión
/// arquitectónica rectora del Plan Maestro (sección 2): "Multi-región: cómputo activo/activo y un
/// único propietario de escritura por agregado o bounded context".
/// </summary>
/// <param name="Value">
/// Clave estable de la región (p. ej. <c>"eu-west"</c>, <c>"us-east"</c>). Debe ser determinística y
/// no debe cambiar una vez asignada a un tenant: renombrar un <see cref="RegionId"/> existente
/// equivale, desde la perspectiva de cualquier índice/caché que lo use como clave, a mover todos sus
/// tenants a una región "nueva" — una migración real de datos entre regiones, fuera del alcance de
/// F5-02 (que solo define el mapa de ownership y el mecanismo de validación).
/// </param>
public readonly record struct RegionId(string Value)
{
    /// <summary>
    /// Región primaria/por defecto: todo tenant sin una fila de asignación explícita en
    /// <see cref="ITenantRegionMapStore"/> resuelve aquí de forma determinística. Igual tratamiento
    /// que <see cref="ShardId.Shared"/> (T1): "sin asignación explícita" nunca significa "región
    /// indeterminada" ni introduce aleatoriedad — significa, siempre, "la región primaria es la
    /// propietaria". En un despliegue de una sola región (el único escenario real disponible hoy en
    /// este repositorio, ver <c>docs/bia-fase5.md</c>), <see cref="ICurrentRegionProvider"/> se
    /// configura también con <see cref="Primary"/>, de modo que <c>RegionalOwnershipBehavior</c>
    /// (Shared.Application) nunca rechaza ninguna escritura — cero cambio de comportamiento para un
    /// consumidor de un solo host/región.
    /// </summary>
    public static readonly RegionId Primary = new("primary");

    public override string ToString() => Value;
}
