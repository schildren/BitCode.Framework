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
