using Microsoft.AspNetCore.Identity;

namespace BitCode.Framework.Shared.Infrastructure.Security.Identity;

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
    }

    public ApplicationRole(string roleName) : base(roleName)
    {
    }
}
