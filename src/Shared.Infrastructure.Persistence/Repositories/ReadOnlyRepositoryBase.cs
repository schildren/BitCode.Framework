using System.Linq.Expressions;
using BitCode.Framework.Shared.Domain.Specifications;
using BitCode.Framework.Shared.Infrastructure.Persistence.Specifications;
using BitCode.Framework.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación de <see cref="RepositoryBase{TEntity, TId}"/> registrada exclusivamente detrás de
/// <c>IReadRepository&lt;TEntity, TId&gt;</c> (F1-17). Un <c>IQuery</c> nunca muta datos (regla dura
/// #2 de <c>docs/convenciones.md</c>): como esta clase solo se resuelve para ese contrato de solo
/// lectura, es seguro forzar <c>AsNoTracking()</c> en todas sus lecturas sin importar si quien
/// escribió la <see cref="ISpecification{T}"/> se olvidó de llamar <c>ApplyAsNoTracking()</c> — el
/// change tracker de EF Core no tiene ningún beneficio para una entidad que jamás se va a modificar
/// ni pasar a <c>SaveChangesAsync</c>, y sí un costo de memoria/CPU evitable.
/// <see cref="RepositoryBase{TEntity, TId}"/> (registrada detrás de <c>IRepository&lt;,&gt;</c>) sigue
/// trackeando por defecto: la usa el lado de escritura, que necesita <c>GetByIdAsync</c> trackeado
/// para poder llamar <c>Update</c> sobre la misma instancia.
/// </summary>
public sealed class ReadOnlyRepositoryBase<TEntity, TId>(DbContext dbContext) : RepositoryBase<TEntity, TId>(dbContext)
    where TEntity : Entity<TId>
    where TId : IEquatable<TId>
{
    public override async Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default) =>
        await DbSet.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id.Equals(id), cancellationToken);

    public override async Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default) =>
        await DbSet.AsNoTracking().ToListAsync(cancellationToken);

    public override async Task<IReadOnlyList<TEntity>> ListAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default) =>
        await SpecificationEvaluator<TEntity>.GetQuery(DbSet, specification)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public override async Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TResult>> selector,
        CancellationToken cancellationToken = default) =>
        await SpecificationEvaluator<TEntity>.GetQuery(DbSet, specification)
            .AsNoTracking()
            .Select(selector)
            .ToListAsync(cancellationToken);

    public override async Task<PagedResult<TEntity>> ListPagedAsync(
        ISpecification<TEntity> specification,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePaging(page, pageSize);

        var filteredQuery = SpecificationEvaluator<TEntity>.GetQuery(DbSet, specification, applyPaging: false)
            .AsNoTracking();
        var totalCount = await filteredQuery.CountAsync(cancellationToken);
        var items = await filteredQuery
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, normalizedPage, normalizedPageSize, totalCount);
    }

    public override async Task<PagedResult<TResult>> ListPagedAsync<TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TResult>> selector,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePaging(page, pageSize);

        var filteredQuery = SpecificationEvaluator<TEntity>.GetQuery(DbSet, specification, applyPaging: false)
            .AsNoTracking();
        var totalCount = await filteredQuery.CountAsync(cancellationToken);
        var items = await filteredQuery
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(selector)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(items, normalizedPage, normalizedPageSize, totalCount);
    }
}
