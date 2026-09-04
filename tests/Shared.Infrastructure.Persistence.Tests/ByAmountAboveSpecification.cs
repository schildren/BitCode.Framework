using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class ByAmountAboveSpecification : Specification<TestEntity>
{
    public ByAmountAboveSpecification(int threshold, int? skip = null, int? take = null)
    {
        ApplyCriteria(e => e.Amount > threshold);
        ApplyOrderBy(e => e.Name);

        if (skip is not null && take is not null)
        {
            ApplyPaging(skip.Value, take.Value);
        }
    }
}
