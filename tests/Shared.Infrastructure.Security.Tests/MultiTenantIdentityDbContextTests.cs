using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

public class MultiTenantIdentityDbContextTests
{
    [Fact]
    public async Task Database_EnsureCreated_BuildsModelWithIdentityAndAppTablesSuccessfully()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestIdentityDbContext>().UseSqlite(connection).Options;
        using var context = new TestIdentityDbContext(options, new FakeTenantProvider(Guid.NewGuid()));

        var act = async () => await context.Database.EnsureCreatedAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task QueryFilter_IsolatesApplicationUsersPerTenant()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestIdentityDbContext>().UseSqlite(connection).Options;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var seed = new TestIdentityDbContext(options, new FakeTenantProvider(tenantA)))
        {
            await seed.Database.EnsureCreatedAsync();
        }

        using (var contextA = new TestIdentityDbContext(options, new FakeTenantProvider(tenantA)))
        {
            contextA.Users.Add(new TestApplicationUser { UserName = "userA", TenantId = tenantA });
            await contextA.SaveChangesAsync();
        }

        using (var contextB = new TestIdentityDbContext(options, new FakeTenantProvider(tenantB)))
        {
            contextB.Users.Add(new TestApplicationUser { UserName = "userB", TenantId = tenantB });
            await contextB.SaveChangesAsync();
        }

        using var readAsTenantA = new TestIdentityDbContext(options, new FakeTenantProvider(tenantA));
        var visible = await readAsTenantA.Users.ToListAsync();

        visible.Should().ContainSingle(u => u.UserName == "userA");
        visible.Should().NotContain(u => u.UserName == "userB");
    }
}
