using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class TestEntity : Entity<Guid>, IAuditedEntity, ISoftDelete, ITenantEntity
{
    public string Name { get; set; } = string.Empty;

    public int Amount { get; set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public string? DeletedBy { get; set; }

    public TestEntity(Guid id, string name, int amount) : base(id)
    {
        Name = name;
        Amount = amount;
    }

    private TestEntity()
    {
    }
}
