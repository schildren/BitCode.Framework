using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Query;

/// <summary>
/// Marcador interno (fix post-revisión de arquitectura de F2-20, Hallazgo 1, CRÍTICO): un único registro
/// (<c>TryAddSingleton</c>) que indica que <see cref="AuditQueryServiceCollectionExtensions.AddSharedAuditQuery"/>
/// resolvió <see cref="IAuditReader"/> por defecto sobre <see cref="InMemoryAuditWriter"/> porque, en ese
/// momento, NINGÚN proyecto había registrado todavía su propia implementación de <see cref="IAuditReader"/>.
/// <see cref="AuditRedactionServiceCollectionExtensions.AddAuditWriter{TWriter}"/> lo consulta para detectar
/// el caso simétrico: si se llama DESPUÉS con un writer real distinto de <see cref="InMemoryAuditWriter"/>,
/// el <see cref="IAuditReader"/> por defecto ya resuelto quedaría huérfano (apuntando a un almacenamiento
/// que ya no recibe escrituras) sin ningún error visible -- exactamente el mismo patrón de hueco que
/// <see cref="AuditRedactionAppliedMarker"/> ya resuelve para la redacción (Hallazgo 1 de F2-19).
/// </summary>
internal sealed class AuditQueryDefaultReaderAppliedMarker;

/// <summary>
/// Registra la "API administrativa" de consulta de auditoría (F2-20, Épica F2-D): <see cref="IAuditReader"/>
/// e <see cref="IAuditQueryService"/>. Deliberadamente un método SEPARADO de
/// <see cref="AuditServiceCollectionExtensions.AddSharedAuditing"/> -- mismo principio que
/// <c>AddSharedAuditRedaction</c>/<c>AddSharedAuditWormExport</c>/<c>AddSharedAuditBatchSigning</c>: la
/// auditoría básica (F2-15) no requiere ninguna capacidad de consulta administrativa para funcionar, así
/// que exponerla es opt-in.
/// </summary>
public static class AuditQueryServiceCollectionExtensions
{
    /// <summary>
    /// Debe llamarse DESPUÉS de <see cref="AuditServiceCollectionExtensions.AddSharedAuditing"/> y de
    /// <see cref="Permissions.PermissionEvaluationServiceCollectionExtensions.AddSharedPermissionEvaluation"/>
    /// (o de <c>AddSharedSecurity</c>/<c>AddSharedOidcAuthentication</c>, que ya la llaman) -- necesita
    /// <c>IPermissionEvaluator</c> (F2-07) registrado para poder evaluar el permiso RBAC dedicado
    /// (<see cref="AuditQueryService.RequiredPermission"/>, <c>"auditoria.consultar"</c>). Lanza
    /// <see cref="InvalidOperationException"/> en el arranque si <see cref="InMemoryAuditWriter"/> no fue
    /// registrado por <see cref="AuditServiceCollectionExtensions.AddSharedAuditing"/>.
    /// <para>
    /// Registra <see cref="InMemoryAuditWriter"/> como implementación por defecto de <see cref="IAuditReader"/>
    /// (<c>TryAddSingleton</c>, la MISMA instancia singleton que ya expone <see cref="IAuditWriter"/> --
    /// buscable inmediatamente lo que se escriba, incluso si <see cref="IAuditWriter"/> terminó decorado por
    /// <c>RedactingAuditWriter</c>, F2-19). Un proyecto que conectó su propio <see cref="IAuditWriter"/>
    /// productivo (tabla SQL, event store, sin relación con <see cref="InMemoryAuditWriter"/>) debe registrar
    /// su propia implementación de <see cref="IAuditReader"/> ANTES de llamar a este método -- gana la
    /// resolución (<c>TryAddSingleton</c> no reemplaza un registro ya existente), mismo principio que
    /// <c>AddSharedAuditWormExport</c> sobre <c>IWormStorage</c>.
    /// </para>
    /// <para>
    /// <see cref="IAuditQueryService"/> se registra <c>Scoped</c> (depende de <c>ITenantContext</c>/
    /// <c>IPermissionEvaluator</c>, ambos <c>Scoped</c>) con <see cref="AuditQueryService"/>. La
    /// exportación (<see cref="IAuditQueryService.ExportAsync"/>) toma <see cref="Worm.IAuditWormExportPipeline"/>
    /// (F2-18) como dependencia OPCIONAL -- si el proyecto no llamó a <c>AddSharedAuditWormExport</c>,
    /// <see cref="AuditQueryService"/> sigue resolviendo (búsqueda funciona igual) y solo
    /// <see cref="IAuditQueryService.ExportAsync"/> devuelve un <c>Result</c> fallido explícito, sin ninguna
    /// excepción de arranque.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSharedAuditQuery(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(InMemoryAuditWriter)))
        {
            throw new InvalidOperationException(
                "AddSharedAuditQuery (F2-20) debe llamarse después de AddSharedAuditing. Si el proyecto ya " +
                "reemplazó IAuditWriter por su propia implementación productiva (tabla SQL, event store), " +
                "registre su propia implementación de IAuditReader antes de llamar a este método -- " +
                "InMemoryAuditWriter no participa en ese caso.");
        }

        // Fix post-revisión de arquitectura de F2-20 (Hallazgo 1, CRÍTICO): el guard de arriba solo
        // detectaba si InMemoryAuditWriter fue registrado ALGUNA VEZ como tipo concreto por
        // AddSharedAuditing -- pero AddAuditWriter<TWriter> (F2-19) NO lo remueve al reemplazar IAuditWriter
        // por un writer productivo real, así que ese guard nunca lanzaba en el escenario de falla real
        // (AddSharedAuditing -> AddAuditWriter<TWriter> -> AddSharedAuditQuery, sin IAuditReader propio):
        // TryAddSingleton de más abajo resolvía en silencio IAuditReader sobre InMemoryAuditWriter, que en
        // ese escenario nunca recibe escrituras reales -- toda búsqueda/exportación devolvía siempre
        // resultados vacíos, sin ninguna excepción de arranque ni de request.
        //
        // Si el proyecto YA registró su propia implementación de IAuditReader, se respeta tal cual (mismo
        // criterio que el TryAddSingleton de abajo). Si NO la registró, se comprueba si AddAuditWriter<T> ya
        // conectó un writer real distinto de InMemoryAuditWriter (AuditWriterRegistrationMarker) -- en ese
        // caso el default sobre InMemoryAuditWriter sería exactamente el hueco descripto arriba, así que se
        // falla en el arranque en lugar de resolver en silencio.
        var hasExplicitReader = services.Any(d => d.ServiceType == typeof(IAuditReader));
        if (!hasExplicitReader)
        {
            var writerMarker = services
                .FirstOrDefault(d => d.ServiceType == typeof(AuditWriterRegistrationMarker))
                ?.ImplementationInstance as AuditWriterRegistrationMarker;

            if (writerMarker is not null && writerMarker.WriterType != typeof(InMemoryAuditWriter))
            {
                throw new InvalidOperationException(
                    $"AddSharedAuditQuery (F2-20) detectó que IAuditWriter fue reemplazado por " +
                    $"{writerMarker.WriterType.Name} (vía AddAuditWriter<TWriter>, F2-19) pero no se registró " +
                    "ninguna implementación propia de IAuditReader. InMemoryAuditWriter ya no recibe " +
                    $"escrituras reales en este escenario -- registre su propio IAuditReader (que lea del " +
                    $"mismo almacenamiento que {writerMarker.WriterType.Name}) ANTES de llamar a " +
                    "AddSharedAuditQuery.");
            }

            // Ningún writer real conectado todavía (o AddAuditWriter<TWriter> se llama DESPUÉS que este
            // método) -- es seguro, por ahora, resolver IAuditReader sobre InMemoryAuditWriter como último
            // recurso. Se deja esta marca para que AddAuditWriter<TWriter>, si se llama MÁS TARDE con un
            // writer real distinto, pueda detectar el mismo hueco en el orden inverso (ver
            // AuditQueryDefaultReaderAppliedMarker).
            services.TryAddSingleton<AuditQueryDefaultReaderAppliedMarker>();
        }

        services.TryAddSingleton<IAuditReader>(sp => sp.GetRequiredService<InMemoryAuditWriter>());
        services.TryAddScoped<IAuditQueryService, AuditQueryService>();

        return services;
    }
}
