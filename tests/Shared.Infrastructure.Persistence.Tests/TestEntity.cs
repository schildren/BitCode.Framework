using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class TestEntity : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;

    public int Amount { get; set; }

    public TestEntity(Guid id, string name, int amount) : base(id)
    {
        Name = name;
        Amount = amount;
    }

    private TestEntity()
    {
    }
}
