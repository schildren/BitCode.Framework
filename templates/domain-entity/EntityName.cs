using BitCode.Framework.Shared.Kernel;

namespace MyApp.Domain.Entities;

public class EntityName : Entity<Guid>, IAuditedEntity, ISoftDelete
//#if (MultiTenant)
    , ITenantEntity
//#endif
{
    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public string? DeletedBy { get; set; }

//#if (MultiTenant)
    public Guid TenantId { get; set; }

//#endif
    public EntityName(Guid id) : base(id)
    {
    }

    private EntityName()
    {
    }
}
