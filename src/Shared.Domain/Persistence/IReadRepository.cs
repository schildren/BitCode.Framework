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

    Task<int> CountAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
}
