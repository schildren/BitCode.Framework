using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.ImportExport.Importacion;

/// <summary>
/// Punto de extensión pluggable que un consumidor real implementa por CADA tipo de importación que quiera
/// soportar (Fase 6, módulo 10) -- mismo espíritu que <c>IIntegrationConnectorSender</c> (Fase 6, módulo 9)
/// o un canal de <c>INotificationChannelSender</c> (módulo 8): este módulo NO conoce ningún modelo de
/// negocio concreto (clientes, productos, etc.), solo sabe leer un CSV en chunks, validar columnas
/// mínimas, aislar errores por fila y persistir el progreso -- la interpretación semántica de cada fila
/// (qué validar más allá de "la columna no está vacía" y cómo aplicarla al modelo de negocio del
/// consumidor) es responsabilidad exclusiva de esta interfaz. Un host real la implementa y la registra en
/// su contenedor de DI (<c>services.AddScoped&lt;IImportRowHandler, MiHandler&gt;()</c>) --
/// <c>Procesamiento.ImportBatchProcessorJob</c> resuelve <c>IEnumerable&lt;IImportRowHandler&gt;</c> y elige
/// la que coincide con <see cref="TipoImportacion"/>.
/// </summary>
/// <remarks>
/// <b>Persistencia propia, fuera del pipeline CQRS del framework:</b> a diferencia de un
/// <c>IRequestHandler</c> de MediatR (regla dura 1, docs/convenciones.md: nunca llama
/// <c>SaveChangesAsync</c> explícito), una implementación de esta interfaz corre DENTRO de
/// <c>ImportBatchProcessorJob</c> -- un worker de infraestructura sin <c>TransactionBehavior</c>, igual que
/// <c>IIntegrationConnectorSender</c> -- y es responsable de confirmar sus propios cambios (por ejemplo,
/// con su propio <c>DbContext</c>/<c>SaveChangesAsync</c>) antes de devolver <see cref="Result.Success"/>.
/// Debe ser IDEMPOTENTE: la misma fila puede reprocesarse tras una caída a mitad de un chunk (ver el
/// <c>remarks</c> de <see cref="ImportJob"/>, sección "Reanudación") -- un upsert por clave natural es el
/// patrón recomendado, nunca un <c>INSERT</c> que falle con una violación de duplicado ante un reproceso
/// legítimo.
/// </remarks>
public interface IImportRowHandler
{
    /// <summary>Código que un cliente HTTP usa en <c>IniciarImportacionCommand.TipoImportacion</c> para
    /// elegir esta implementación -- único por host (dos implementaciones con el mismo código es un error
    /// de configuración del consumidor, no de este módulo).</summary>
    string TipoImportacion { get; }

    /// <summary>Nombres de columna (deben existir en el encabezado del CSV, sin distinguir mayúsculas)
    /// cuya ausencia o vacío en una fila la clasifica como error DE ESA FILA, sin siquiera invocar
    /// <see cref="ProcesarFilaAsync"/> -- validación mínima aplicada por el propio job, ver el
    /// <c>remarks</c> de <c>Procesamiento.ImportBatchProcessorJob</c>.</summary>
    IReadOnlyList<string> ColumnasRequeridas { get; }

    /// <summary>Valida y aplica UNA fila ya materializada como diccionario columna-a-valor (claves
    /// insensibles a mayúsculas, tomadas del encabezado real del archivo). <paramref name="tenantId"/> es
    /// el tenant dueño del <see cref="ImportJob"/> -- <c>ImportBatchProcessorJob</c> corre cross-tenant
    /// (sin <c>HttpContext</c>/<c>ITenantProvider</c> resoluble), así que esta implementación DEBE aplicar
    /// el aislamiento multi-tenant de su propio modelo de negocio explícitamente con este valor, nunca
    /// asumiendo un tenant "actual" ambiental. Cualquier excepción no controlada que esta implementación
    /// deje escapar es capturada por el job (aislamiento por ítem) y tratada como un error de esa fila --
    /// igual que un <c>Result.Failure</c> explícito.</summary>
    Task<Result> ProcesarFilaAsync(Guid tenantId, IReadOnlyDictionary<string, string> fila, CancellationToken cancellationToken);
}
