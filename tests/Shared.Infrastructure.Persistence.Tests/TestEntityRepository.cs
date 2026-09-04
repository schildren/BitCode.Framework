using BitCode.Framework.Shared.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class TestEntityRepository(TestDbContext dbContext)
    : RepositoryBase<TestEntity, Guid>(dbContext), ITestEntityRepository
{
    public async Task<TestEntity?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        await DbSet.FirstOrDefaultAsync(e => e.Name == name, cancellationToken);
}
