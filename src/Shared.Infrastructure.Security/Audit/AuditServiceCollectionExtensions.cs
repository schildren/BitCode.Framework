using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Registra <see cref="IAuditWriter"/> (F2-15, Épica F2-D) y <see cref="IAuditIntegrityVerifier"/> (F2-16,
/// Épica F2-D).
/// </summary>
public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="InMemoryAuditWriter"/> como implementación por defecto de
    /// <see cref="IAuditWriter"/> (<c>TryAddSingleton</c> -- singleton porque el almacenamiento en memoria
    /// necesita sobrevivir al scope de un request individual; una implementación persistente real que
    /// dependa de un <c>DbContext</c> por scope debe registrarse explícitamente como <c>Scoped</c> por el
    /// proyecto consumidor). Llamar a este método NO impide que un proyecto consumidor reemplace la
    /// implementación: un <c>AddScoped&lt;IAuditWriter, TImplementacionPropia&gt;()</c> llamado DESPUÉS de
    /// <c>AddSharedAuditing</c> gana la resolución (último registro para el mismo tipo de servicio, mismo
    /// principio que <c>AddSharedPermissionEvaluation</c>/F2-07) sin necesitar <c>Replace</c> explícito.
    /// <para>
    /// También registra <see cref="AuditIntegrityVerifier"/> como implementación por defecto de
    /// <see cref="IAuditIntegrityVerifier"/> (F2-16). A diferencia de <see cref="IAuditWriter"/>, este
    /// servicio no tiene estado ni depende del almacenamiento elegido -- opera solo sobre la secuencia de
    /// <see cref="AuditEntry"/> que el llamador le provea, cualquiera sea su origen (memoria, SQL, un
    /// destino WORM de F2-18) -- por lo que un proyecto consumidor normalmente no necesita reemplazarlo; se
    /// registra igual con <c>TryAddSingleton</c> para permitir el mismo patrón de reemplazo si alguna vez
    /// hiciera falta.
    /// </para>
    /// <para>
    /// F2-20 (fix menor de registro, sin cambio de comportamiento observable): registra el propio tipo
    /// concreto <see cref="InMemoryAuditWriter"/> como singleton y mapea <see cref="IAuditWriter"/> a esa
    /// misma instancia vía factory, en lugar del <c>TryAddSingleton&lt;IAuditWriter,
    /// InMemoryAuditWriter&gt;()</c> anterior. Esto permite que
    /// <see cref="Query.AuditQueryServiceCollectionExtensions.AddSharedAuditQuery"/> (F2-20) resuelva
    /// <see cref="Query.IAuditReader"/> sobre el almacenamiento REAL (<see cref="InMemoryAuditWriter"/>)
    /// incluso si <see cref="IAuditWriter"/> terminó decorado por <c>RedactingAuditWriter</c> (F2-19) --
    /// leer siempre la fuente de verdad subyacente es correcto y seguro porque la redacción ya se aplicó
    /// ANTES de escribir (F2-19). No es un cambio observable para ningún consumidor existente: "último
    /// registro gana" para <see cref="IAuditWriter"/> sigue funcionando igual (ver
    /// <c>AuditServiceCollectionExtensionsTests.AddSharedAuditing_UnaImplementacionPropiaRegistradaDespues_GanaLaResolucion</c>).
    /// </para>
    /// </summary>
    public static IServiceCollection AddSharedAuditing(this IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryAuditWriter>();
        services.TryAddSingleton<IAuditWriter>(sp => sp.GetRequiredService<InMemoryAuditWriter>());
        services.TryAddSingleton<IAuditIntegrityVerifier, AuditIntegrityVerifier>();
        return services;
    }
}
