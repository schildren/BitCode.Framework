namespace BitCode.Framework.Shared.Infrastructure.Security.FeatureFlags;

/// <summary>
/// Evalúa feature flags simples on/off (F4-12, Fase 4 -- Runtime de alta disponibilidad). Deliberadamente
/// acotado a lo que la Fase 4 necesita a nivel de runtime/infraestructura: un interruptor booleano leído
/// de configuración externalizada, sin segmentación por usuario/tenant, sin porcentaje de rollout y sin
/// panel administrativo -- el Plan Maestro (sección "Módulos", Fase 6, módulo 4 "Feature Management")
/// reserva esa capacidad rica (flags, segmentos, rollout y auditoría con actor humano identificado) para
/// un bounded context propio de Fase 6, con su propio ownership de datos/API/eventos. Este contrato NO
/// debe crecer para cubrir ese alcance -- un proyecto consumidor que necesite targeting por usuario o
/// rollout gradual antes de que exista el módulo de Fase 6 debe evaluarlo explícitamente en su propio
/// código de aplicación, no extendiendo esta interfaz.
/// </summary>
public interface IFeatureFlagProvider
{
    /// <summary>
    /// Evalúa el estado actual del flag <paramref name="flagName"/>. Lee siempre el valor vigente en
    /// configuración en el momento de la llamada (nunca un valor cacheado por el propio contrato) -- si el
    /// proyecto consumidor externaliza el flag vía un <c>ConfigMap</c> con recarga automática
    /// (<c>reloadOnChange: true</c>, comportamiento nativo de <c>Microsoft.Extensions.Configuration</c>),
    /// un cambio aplicado fuera del proceso (por ejemplo, <c>kubectl apply</c>) queda reflejado en la
    /// siguiente llamada sin reiniciar el pod. Comparación de <paramref name="flagName"/> insensible a
    /// mayúsculas/minúsculas (mismo criterio que las claves de <c>IConfiguration</c>).
    /// </summary>
    /// <param name="flagName">Nombre lógico del flag (por ejemplo, <c>"NuevoFlujoDePagos"</c>).</param>
    /// <param name="defaultValue">
    /// Valor a devolver si <paramref name="flagName"/> no está declarado en configuración -- un flag
    /// ausente nunca lanza una excepción ni se trata como error, se resuelve a este valor explícito.
    /// </param>
    bool IsEnabled(string flagName, bool defaultValue = false);
}
