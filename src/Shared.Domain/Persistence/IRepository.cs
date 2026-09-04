using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Domain.Persistence;

public interface IRepository<TEntity, TId> : IReadRepository<TEntity, TId>
    where TEntity : Entity<TId>
    where TId : IEquatable<TId>
{
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    void Update(TEntity entity);

    void Remove(TEntity entity);
}
