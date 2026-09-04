using BitCode.Framework.Shared.Domain.Persistence;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public interface ITestEntityRepository : IRepository<TestEntity, Guid>
{
    Task<TestEntity?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}
