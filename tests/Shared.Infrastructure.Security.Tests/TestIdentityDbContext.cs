using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

public class TestApplicationUser : ApplicationUser;

public class TestApplicationRole : ApplicationRole;

public class TestIdentityDbContext(
    DbContextOptions<TestIdentityDbContext> options,
    ITenantProvider tenantProvider)
    : MultiTenantIdentityDbContext<TestApplicationUser, TestApplicationRole>(options, tenantProvider);
