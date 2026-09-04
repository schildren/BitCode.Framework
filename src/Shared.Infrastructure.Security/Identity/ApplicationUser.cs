using BitCode.Framework.Shared.Kernel;
using Microsoft.AspNetCore.Identity;

namespace BitCode.Framework.Shared.Infrastructure.Security.Identity;

public class ApplicationUser : IdentityUser<Guid>, IAuditedEntity, ISoftDelete, ITenantEntity
{
    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public string? DeletedBy { get; set; }

    public Guid TenantId { get; set; }
}
