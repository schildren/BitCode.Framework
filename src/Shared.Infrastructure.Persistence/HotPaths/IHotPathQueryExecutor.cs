using BitCode.Framework.Shared.Domain.HotPaths;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.HotPaths;

/// <summary>
/// Único punto de entrada permitido para ejecutar un <see cref="IHotPathQuery{TResult}"/> (F1-18).
/// Un handler de aplicación inyecta este contrato — nunca un <c>DbContext</c> directamente (regla
/// dura #5 de <c>docs/convenciones.md</c>) — igual que ya inyecta <c>IReadRepository&lt;,&gt;</c> o
/// <c>IUnitOfWork</c> en vez del <c>DbContext</c> crudo.
/// </summary>
public interface IHotPathQueryExecutor
{
    /// <summary>
    /// Ejecuta la consulta y devuelve todas las filas materializadas como <typeparamref name="TResult"/>.
    /// </summary>
    Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        IHotPathQuery<TResult> query,
        CancellationToken cancellationToken = default)
        where TResult : class;

    /// <summary>
    /// Ejecuta la consulta esperando exactamente una fila (caso típico de un resumen/agregado) y
    /// devuelve ese único <typeparamref name="TResult"/> materializado, o <c>null</c> si la consulta
    /// no devolvió filas.
    /// </summary>
    Task<TResult?> SingleOrDefaultAsync<TResult>(
        IHotPathQuery<TResult> query,
        CancellationToken cancellationToken = default)
        where TResult : class;
}
