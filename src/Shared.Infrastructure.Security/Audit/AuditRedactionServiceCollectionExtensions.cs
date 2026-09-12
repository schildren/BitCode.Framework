using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Marcador interno (F2-19, fix post-revisión de arquitectura -- Hallazgo 2): un único registro
/// (<c>TryAddSingleton</c>) que indica que <see cref="AuditRedactionServiceCollectionExtensions.AddSharedAuditRedaction"/>
/// ya decoró <see cref="IAuditWriter"/> con <see cref="RedactingAuditWriter"/> en algún momento de la
/// composición de servicios -- independientemente de qué <see cref="IAuditWriter"/> "interno" haya envuelto
/// en ese momento. Reemplaza la comprobación de idempotencia anterior
/// (<c>d.ImplementationType == typeof(RedactingAuditWriter)</c>), que NUNCA era verdadera: el decorador se
/// registra vía <c>ServiceDescriptor.Describe</c> con una factory (no un tipo concreto), y para un
/// descriptor basado en factory <see cref="ServiceDescriptor.ImplementationType"/> es siempre <c>null</c> --
/// mismo motivo por el que <c>PermissionEvaluationServiceCollectionExtensions.IsPermissionCacheAlreadyApplied</c>
/// (F2-09) usa <see cref="ServiceDescriptor.ImplementationFactory"/> como marcador en vez de
/// <c>ImplementationType</c>. Acá se usa un tipo dedicado en lugar de esa misma comprobación sobre
/// <see cref="IAuditWriter"/> porque <see cref="AuditRedactionServiceCollectionExtensions.AddAuditWriter{TWriter}"/>
/// TAMBIÉN registra <see cref="IAuditWriter"/> con una factory en el caso no decorado -- comprobar solo
/// "hay una factory" sería ambiguo entre "ya está decorado" y "hay un writer real registrado sin decorar
/// todavía".
/// </summary>
internal sealed class AuditRedactionAppliedMarker;

/// <summary>
/// Marcador interno (fix post-revisión de arquitectura de F2-20, Hallazgo 1, CRÍTICO): registra qué tipo
/// concreto de <see cref="IAuditWriter"/> quedó realmente conectado la última vez que se llamó a
/// <see cref="AuditRedactionServiceCollectionExtensions.AddAuditWriter{TWriter}"/> -- <c>TryAddSingleton</c>
/// (un único registro, "primero gana" para efectos de esta detección; el ORDEN de llamadas de
/// <c>AddAuditWriter&lt;TWriter&gt;</c> no importa para lo que este marcador necesita saber: si el proyecto
/// alguna vez conectó un writer real distinto de <see cref="InMemoryAuditWriter"/>).
/// <para>
/// <see cref="Query.AuditQueryServiceCollectionExtensions.AddSharedAuditQuery"/> (F2-20) lo usa para
/// detectar el hueco de raíz exacto que <c>AddSharedAuditRedaction</c> ya resolvía para la redacción
/// (Hallazgo 1 de F2-19) pero que seguía abierto para <c>IAuditReader</c>: si <see cref="IAuditWriter"/> fue
/// reemplazado por un writer productivo real (tabla SQL, event store) y NINGÚN proyecto registró su propia
/// implementación de <see cref="Query.IAuditReader"/>, <c>AddSharedAuditQuery</c> NO debe resolver
/// silenciamente <see cref="Query.IAuditReader"/> sobre <see cref="InMemoryAuditWriter"/> -- ese
/// almacenamiento nunca recibiría las escrituras reales, y toda búsqueda/exportación de auditoría
/// devolvería siempre resultados vacíos sin ninguna excepción visible.
/// </para>
/// </summary>
internal sealed class AuditWriterRegistrationMarker(Type writerType)
{
    public Type WriterType { get; } = writerType;
}

/// <summary>
/// Registra la redacción de PII de F2-19 (Épica F2-D): decora el <see cref="IAuditWriter"/> ya registrado
/// (por <see cref="AuditServiceCollectionExtensions.AddSharedAuditing"/>, o por el propio proyecto
/// consumidor) con <see cref="RedactingAuditWriter"/> -- mismo principio que
/// <c>PermissionCacheServiceCollectionExtensions.AddSharedPermissionCache</c> (F2-09) decorando
/// <c>IPermissionService</c>.
/// </summary>
public static class AuditRedactionServiceCollectionExtensions
{
    /// <summary>
    /// Debe llamarse DESPUÉS de <see cref="AuditServiceCollectionExtensions.AddSharedAuditing"/> (o de
    /// cualquier registro propio de <see cref="IAuditWriter"/>) -- necesita que el servicio ya esté
    /// registrado para poder decorarlo. Lanza <see cref="InvalidOperationException"/> en el arranque (nunca
    /// en tiempo de request) si no encuentra ningún registro previo.
    /// <para>
    /// Deliberadamente un método SEPARADO de <c>AddSharedAuditing</c> -- igual que
    /// <c>AddSharedPermissionCache</c> es opt-in respecto de <c>AddSharedPermissionEvaluation</c> -- para
    /// que un proyecto que ya tiene su propia política de redacción/DLP externa pueda registrar su propio
    /// <see cref="IAuditWriter"/> sin que este método le imponga la política por defecto de
    /// <see cref="AuditRedactionPolicy"/>. Un proyecto nuevo típico, sin ninguna necesidad especial,
    /// simplemente llama a ambos métodos en orden.
    /// </para>
    /// <para>
    /// <b>Orden de registro respecto del writer REAL del proyecto consumidor</b>: si el proyecto conecta su
    /// implementación productiva con <see cref="AddAuditWriter{TWriter}"/> (recomendado), el orden entre
    /// ese método y este NO importa -- ambos se coordinan a través de <see cref="AuditRedactionAppliedMarker"/>
    /// para que la redacción siga aplicándose sin importar cuál se llame primero. Si en cambio el proyecto
    /// registra su <see cref="IAuditWriter"/> con un <c>services.AddScoped&lt;IAuditWriter, TWriter&gt;()</c>
    /// manual (sin pasar por <see cref="AddAuditWriter{TWriter}"/>) DESPUÉS de este método, ese registro
    /// manual GANA la resolución (último registro para el mismo tipo de servicio) y el
    /// <see cref="RedactingAuditWriter"/> queda huérfano sin ningún error visible -- exactamente el
    /// Hallazgo 1 de la revisión de arquitectura de F2-19. Usar <see cref="AddAuditWriter{TWriter}"/> en vez
    /// de un registro manual evita ese hueco de raíz.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSharedAuditRedaction(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Idempotente -- mismo criterio que AddSharedPermissionCache: una segunda llamada no debe volver a
        // decorar un IAuditWriter que ya es un RedactingAuditWriter. Ver AuditRedactionAppliedMarker para
        // el porqué de este marcador en vez de inspeccionar el descriptor de IAuditWriter directamente.
        if (services.IsAuditRedactionApplied())
        {
            return services;
        }

        var innerDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IAuditWriter))
            ?? throw new InvalidOperationException(
                "AddSharedAuditRedaction debe llamarse después de registrar IAuditWriter (AddSharedAuditing " +
                "o el registro propio del proyecto consumidor).");

        services.Configure<AuditRedactionOptions>(configuration.GetSection(AuditRedactionOptions.SectionName));
        services.TryAddSingleton<IAuditRedactionPolicy, AuditRedactionPolicy>();
        services.TryAddSingleton<AuditRedactionAppliedMarker>();

        services.Replace(ServiceDescriptor.Describe(
            typeof(IAuditWriter),
            sp => new RedactingAuditWriter(
                (IAuditWriter)ResolveInner(sp, innerDescriptor),
                sp.GetRequiredService<IAuditRedactionPolicy>()),
            innerDescriptor.Lifetime));

        return services;
    }

    /// <summary>
    /// Registra la implementación PRODUCTIVA real de <see cref="IAuditWriter"/> que un proyecto consumidor
    /// conecta (tabla SQL append-only, event store, o el destino WORM de F2-18) -- alternativa RECOMENDADA a
    /// un <c>services.AddScoped&lt;IAuditWriter, TWriter&gt;()</c> manual cuando el proyecto también usa
    /// <see cref="AddSharedAuditRedaction"/>, porque hace que el ORDEN relativo entre ambas llamadas deje de
    /// importar.
    /// </summary>
    /// <remarks>
    /// Fix post-revisión de arquitectura de F2-19 (Hallazgo 1, CRÍTICO): con un registro manual, si el
    /// proyecto conecta su <see cref="IAuditWriter"/> real DESPUÉS de <see cref="AddSharedAuditRedaction"/>
    /// (orden <c>AddSharedAuditing()</c> → <c>AddSharedAuditRedaction(...)</c> →
    /// <c>services.AddScoped&lt;IAuditWriter, TSqlAuditWriter&gt;()</c>), ese registro manual gana la
    /// resolución (último registro para el mismo tipo de servicio) y el <see cref="RedactingAuditWriter"/>
    /// que decoraba el writer anterior (típicamente <see cref="InMemoryAuditWriter"/>) queda huérfano SIN
    /// NINGÚN ERROR VISIBLE en el arranque: el writer real pasaría a recibir <see cref="AuditEntryRequest"/>
    /// SIN REDACTAR, <see cref="AuditHashCalculator.Compute"/> calcularía <see cref="AuditEntry.AuditHash"/>
    /// sobre PII sin redactar, y cualquier firma (F2-17)/exportación WORM (F2-18) posterior heredaría esa
    /// PII. Este método detecta (vía <see cref="AuditRedactionAppliedMarker"/>) si
    /// <see cref="AddSharedAuditRedaction"/> ya se aplicó y, en ese caso, vuelve a aplicar la decoración
    /// automáticamente sobre <typeparamref name="TWriter"/> en lugar de reemplazar el registro sin decorar
    /// -- el orden de llamada entre <see cref="AddSharedAuditRedaction"/> y este método deja de importar. Si
    /// <see cref="AddSharedAuditRedaction"/> todavía no fue llamado (o nunca se llama), este método
    /// simplemente registra <typeparamref name="TWriter"/> como <see cref="IAuditWriter"/> sin decorar,
    /// igual que un <c>AddScoped</c>/<c>Replace</c> manual habría hecho.
    /// </remarks>
    /// <remarks>
    /// Fix post-revisión de arquitectura de F2-20 (Hallazgo 1, CRÍTICO): además registra un
    /// <see cref="AuditWriterRegistrationMarker"/> (para que
    /// <see cref="Query.AuditQueryServiceCollectionExtensions.AddSharedAuditQuery"/> pueda detectar, si se
    /// llama DESPUÉS, que <see cref="IAuditWriter"/> ya no es <see cref="InMemoryAuditWriter"/>) y lanza
    /// <see cref="InvalidOperationException"/> si <c>AddSharedAuditQuery</c> ya se llamó ANTES sin que el
    /// proyecto registrara su propio <see cref="Query.IAuditReader"/> -- ver
    /// <c>docs/guia-auditoria-inmutable.md</c>, sección F2-20, para el detalle completo del hueco cerrado.
    /// </remarks>
    public static IServiceCollection AddAuditWriter<TWriter>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TWriter : class, IAuditWriter
    {
        services.Add(ServiceDescriptor.Describe(typeof(TWriter), typeof(TWriter), lifetime));
        services.TryAddSingleton(new AuditWriterRegistrationMarker(typeof(TWriter)));

        // Fix post-revisión de arquitectura de F2-20 (Hallazgo 1, CRÍTICO): si AddSharedAuditQuery ya se
        // llamó ANTES que este método (orden AddSharedAuditing -> AddSharedAuditQuery -> AddAuditWriter<T>)
        // y en ese momento no había ningún IAuditReader propio registrado, AddSharedAuditQuery ya resolvió
        // (TryAddSingleton, no se puede deshacer) IAuditReader sobre InMemoryAuditWriter -- conectar ahora
        // un writer real distinto dejaría ese IAuditReader apuntando a un almacenamiento que ya no recibe
        // escrituras, sin ningún error visible. Ver Query.AuditQueryDefaultReaderAppliedMarker.
        if (typeof(TWriter) != typeof(InMemoryAuditWriter) &&
            services.Any(d => d.ServiceType == typeof(Query.AuditQueryDefaultReaderAppliedMarker)))
        {
            throw new InvalidOperationException(
                $"AddAuditWriter<{typeof(TWriter).Name}> detectó que AddSharedAuditQuery (F2-20) ya se llamó " +
                "ANTES y ya resolvió IAuditReader por defecto sobre InMemoryAuditWriter (porque en ese " +
                $"momento no había ningún IAuditReader propio registrado). Conectar ahora {typeof(TWriter).Name} " +
                "como IAuditWriter real dejaría ese IAuditReader por defecto apuntando a un almacenamiento que " +
                "ya no recibe escrituras -- toda búsqueda/exportación de auditoría devolvería siempre " +
                "resultados vacíos. Llame a AddSharedAuditQuery DESPUÉS de AddAuditWriter<TWriter>, o registre " +
                "su propia implementación de IAuditReader antes de AddSharedAuditQuery.");
        }

        if (services.IsAuditRedactionApplied())
        {
            services.Replace(ServiceDescriptor.Describe(
                typeof(IAuditWriter),
                sp => new RedactingAuditWriter(
                    sp.GetRequiredService<TWriter>(),
                    sp.GetRequiredService<IAuditRedactionPolicy>()),
                lifetime));

            return services;
        }

        services.Replace(ServiceDescriptor.Describe(
            typeof(IAuditWriter),
            sp => (IAuditWriter)sp.GetRequiredService<TWriter>(),
            lifetime));

        return services;
    }

    /// <summary>
    /// Ver <see cref="AuditRedactionAppliedMarker"/> -- comprobación centralizada para no duplicar el mismo
    /// <c>Any</c> inline en <see cref="AddSharedAuditRedaction"/> y en <see cref="AddAuditWriter{TWriter}"/>.
    /// </summary>
    private static bool IsAuditRedactionApplied(this IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(AuditRedactionAppliedMarker));

    /// <summary>
    /// Construye la instancia "interna" (no decorada) del <see cref="IAuditWriter"/> ya registrado antes de
    /// esta llamada, replicando el mecanismo de resolución del contenedor -- sin pasar por
    /// <c>sp.GetRequiredService&lt;IAuditWriter&gt;()</c>, que a esta altura ya resolvería el propio
    /// decorador (mismo criterio que <c>PermissionCacheServiceCollectionExtensions.ResolveInner</c>).
    /// </summary>
    private static object ResolveInner(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(sp);
        }

        return ActivatorUtilities.GetServiceOrCreateInstance(sp, descriptor.ImplementationType!);
    }
}
