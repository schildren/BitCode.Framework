using System.Linq.Expressions;
using BitCode.Framework.Shared.Domain.Specifications;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Domain.Persistence;

public interface IReadRepository<TEntity, TId>
    where TEntity : Entity<TId>
    where TId : IEquatable<TId>
{
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> ListAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Proyecta directamente a <typeparamref name="TResult"/> (por ejemplo, un DTO de respuesta)
    /// sin materializar la entidad completa: el <c>SELECT</c> generado por EF Core solo trae las
    /// columnas que <paramref name="selector"/> efectivamente usa (F1-17). Preferir este método a
    /// <see cref="ListAsync(ISpecification{TEntity}, CancellationToken)"/> seguido de un
    /// <c>.Select()</c> en memoria: ese patrón sí trae la fila completa desde la base de datos.
    /// </summary>
    Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TResult>> selector,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pagina la especificación dada (sin usar su propio Skip/Take, que quedan reservados para el
    /// caso en que el llamador no use esta primitiva) y devuelve, junto a la página de entidades,
    /// el total de filas que cumplen el filtro — sin traer todas las filas a memoria para contarlas.
    /// </summary>
    Task<PagedResult<TEntity>> ListPagedAsync(
        ISpecification<TEntity> specification,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Combina <see cref="ListPagedAsync(ISpecification{TEntity}, int, int, CancellationToken)"/> con
    /// proyección: la forma recomendada de servir un listado paginado a un endpoint (grilla, tabla)
    /// sin traer nunca la entidad completa.
    /// </summary>
    Task<PagedResult<TResult>> ListPagedAsync<TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TResult>> selector,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
}
