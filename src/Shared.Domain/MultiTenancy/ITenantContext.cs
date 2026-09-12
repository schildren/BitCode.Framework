namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Contrato de más alto nivel que <see cref="ITenantProvider"/> (F1-15): expone el <c>TenantId</c>
/// ya resuelto para el scope/request actual, memoizado la primera vez que se consulta dentro de ese
/// scope. A diferencia de <see cref="ITenantProvider"/> (que puede volver a evaluar el claim en cada
/// acceso), un <see cref="ITenantContext"/> garantiza que, una vez resuelto, el <c>TenantId</c> es
/// estable durante todo el request — ningún componente puede cambiarlo a mitad de camino porque la
/// interfaz no expone ningún miembro de escritura.
/// </summary>
/// <remarks>
/// Usar <see cref="ITenantContext"/> (no <see cref="ITenantProvider"/> directamente) en:
/// <list type="bullet">
/// <item><description>Código de aplicación (handlers, servicios) que necesita leer el
/// <c>TenantId</c> del request actual para lógica de negocio o para incluirlo en logs
/// propios.</description></item>
/// <item><description>Middleware de enriquecimiento de logging (ver
/// <c>TenantLogEnrichmentMiddleware</c>, Shared.Infrastructure.Web), que empuja este valor al
/// contexto estructurado de Serilog una única vez por request.</description></item>
/// </list>
/// <see cref="ITenantProvider"/> sigue siendo el contrato de bajo nivel que consume
/// <c>MultiTenantDbContext</c>/<c>TenantSaveChangesInterceptor</c> para el filtro global de EF Core
/// (F1-12) — <see cref="ITenantContext"/> lo envuelve, no lo reemplaza, para no introducir un cambio
/// disruptivo en un contrato que ya tiene tres implementaciones productivas/de prueba.
/// </remarks>
public interface ITenantContext
{
    /// <summary>
    /// Igual semántica que <see cref="ITenantProvider.IsMultiTenancyEnabled"/>: si es
    /// <see langword="false"/>, el proyecto consumidor no opera en modo multi-tenant y
    /// <see cref="TenantId"/> siempre es <see langword="null"/>.
    /// </summary>
    bool IsMultiTenancyEnabled { get; }

    /// <summary>
    /// TenantId resuelto para el scope/request actual. Se calcula de forma perezosa la primera vez
    /// que se accede y queda fijo (memoizado) para el resto de la vida del scope — no existe ningún
    /// método para reasignarlo.
    /// </summary>
    Guid? TenantId { get; }
}
