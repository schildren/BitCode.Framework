using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Query;

/// <summary>
/// Lectura controlada, filtrada y paginada sobre el almacenamiento real detrás de un <see cref="IAuditWriter"/>
/// (F2-20, Épica F2-D). Cierra el hueco explícito dejado por F2-15/F2-16/F2-18
/// (<c>docs/guia-auditoria-inmutable.md</c>): <c>InMemoryAuditWriter.Entries</c> nunca fue pensado como una
/// API de consulta (sin filtros ni paginación, solo para inspección de desarrollo/pruebas).
/// <para>
/// Deliberadamente separado de <see cref="IAuditWriter"/> -- no toda implementación de escritura necesita
/// (o puede, de forma eficiente) exponer lectura genérica: una tabla SQL append-only real la resuelve con
/// una consulta indexada por sus propios medios, mientras que un event store podría necesitar una
/// proyección de lectura separada. Un proyecto que conecta su propio <see cref="IAuditWriter"/> productivo y
/// quiere usar <see cref="AuditQueryService"/> (F2-20) debe registrar también su propia implementación de
/// esta interfaz -- <see cref="InMemoryAuditWriter"/> es la única implementación por defecto (dev/pruebas).
/// </para>
/// </summary>
public interface IAuditReader
{
    /// <summary>
    /// Nunca lanza una excepción de negocio esperada -- mismo criterio que <see cref="IAuditWriter.WriteAsync"/>.
    /// </summary>
    Task<Result<PagedResult<AuditEntry>>> SearchAsync(AuditSearchFilter filter, CancellationToken cancellationToken = default);
}
