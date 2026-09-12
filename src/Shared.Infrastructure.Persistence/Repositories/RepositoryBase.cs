using System.Linq.Expressions;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Specifications;
using BitCode.Framework.Shared.Infrastructure.Persistence.Specifications;
using BitCode.Framework.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Repositories;

public class RepositoryBase<TEntity, TId>(DbContext dbContext) : IRepository<TEntity, TId>
    where TEntity : Entity<TId>
    where TId : IEquatable<TId>
{
    protected DbContext DbContext { get; } = dbContext;

    protected DbSet<TEntity> DbSet { get; } = dbContext.Set<TEntity>();

    /// <remarks>
    /// Se usa un Where/FirstOrDefault en vez de DbSet.FindAsync a propósito: FindAsync devuelve una
    /// entidad ya trackeada directamente desde el ChangeTracker sin volver a consultar la base, lo
    /// que salta por completo los filtros globales (soft-delete/tenant) cuando esa entidad ya fue
    /// cargada o modificada en la misma unidad de trabajo — devolviendo, por ejemplo, un registro que
    /// acaba de ser soft-eliminado. Se prioriza la consistencia del filtro sobre el atajo de caché
    /// local que ofrece FindAsync.
    /// </remarks>
    public virtual async Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default) =>
        await DbSet.FirstOrDefaultAsync(entity => entity.Id.Equals(id), cancellationToken);

    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default) =>
        await DbSet.ToListAsync(cancellationToken);

    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default) =>
        await SpecificationEvaluator<TEntity>.GetQuery(DbSet, specification).ToListAsync(cancellationToken);

    public virtual async Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TResult>> selector,
        CancellationToken cancellationToken = default) =>
        await SpecificationEvaluator<TEntity>.GetQuery(DbSet, specification)
            .Select(selector)
            .ToListAsync(cancellationToken);

    public virtual async Task<PagedResult<TEntity>> ListPagedAsync(
        ISpecification<TEntity> specification,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePaging(page, pageSize);

        var filteredQuery = SpecificationEvaluator<TEntity>.GetQuery(DbSet, specification, applyPaging: false);
        var totalCount = await filteredQuery.CountAsync(cancellationToken);
        var items = await filteredQuery
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, normalizedPage, normalizedPageSize, totalCount);
    }

    public virtual async Task<PagedResult<TResult>> ListPagedAsync<TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TResult>> selector,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePaging(page, pageSize);

        var filteredQuery = SpecificationEvaluator<TEntity>.GetQuery(DbSet, specification, applyPaging: false);
        var totalCount = await filteredQuery.CountAsync(cancellationToken);
        var items = await filteredQuery
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(selector)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(items, normalizedPage, normalizedPageSize, totalCount);
    }

    /// <summary>
    /// Recorte defensivo de última línea (F1-21): protege la base de datos incluso si algo llegó a
    /// llamar a este método con un <c>page</c>/<c>pageSize</c> crudo sin pasar por
    /// <see cref="PageRequest.Create"/> (el punto de validación principal, que nunca trunca en
    /// silencio: devuelve un <see cref="Result{TValue}"/> fallido con un error de validación claro).
    /// Este recorte SÍ trunca en silencio a propósito — es una red de seguridad de infraestructura,
    /// no la experiencia esperada para un cliente HTTP; todo endpoint de listado debe construir un
    /// <see cref="PageRequest"/> antes de llegar aquí para que un <c>pageSize</c> fuera de rango se
    /// reporte como error 400 en vez de aplicar este límite sin avisar.
    /// </summary>
    protected static (int Page, int PageSize) NormalizePaging(int page, int pageSize) =>
        (page < 1 ? 1 : page, pageSize < 1 ? 1 : pageSize > PageRequest.DefaultMaxPageSize ? PageRequest.DefaultMaxPageSize : pageSize);

    public virtual Task<PagedResult<TEntity>> ListPagedAsync(
        ISpecification<TEntity> specification,
        PageRequest pageRequest,
        CancellationToken cancellationToken = default) =>
        ListPagedAsync(specification, pageRequest.Page, pageRequest.PageSize, cancellationToken);

    public virtual Task<PagedResult<TResult>> ListPagedAsync<TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TResult>> selector,
        PageRequest pageRequest,
        CancellationToken cancellationToken = default) =>
        ListPagedAsync(specification, selector, pageRequest.Page, pageRequest.PageSize, cancellationToken);

    public virtual async Task<int> CountAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default) =>
        await SpecificationEvaluator<TEntity>.GetQuery(DbSet, specification).CountAsync(cancellationToken);

    public virtual async Task<bool> AnyAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default) =>
        await SpecificationEvaluator<TEntity>.GetQuery(DbSet, specification).AnyAsync(cancellationToken);

    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        await DbSet.AddAsync(entity, cancellationToken);

    public virtual void Update(TEntity entity) => DbSet.Update(entity);

    public virtual void Remove(TEntity entity) => DbSet.Remove(entity);
}
