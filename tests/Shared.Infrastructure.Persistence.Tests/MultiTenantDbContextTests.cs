using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class MultiTenantDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<MultiTenantTestDbContext> _options;

    public MultiTenantDbContextTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<MultiTenantTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var seedContext = new MultiTenantTestDbContext(_options, new FakeTenantProvider(null, isMultiTenancyEnabled: false));
        seedContext.Database.EnsureCreated();
    }

    // Reutiliza deliberadamente las mismas DbContextOptions (mismo modelo cacheado por EF Core)
    // en instancias construidas con ITenantProvider distintos, para comprobar empíricamente
    // que el filtro global no "congela" el tenant de la primera instancia creada.
    [Fact]
    public async Task QueryFilter_IsolatesDataPerTenantAcrossDifferentContextInstances()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var contextA = new MultiTenantTestDbContext(_options, new FakeTenantProvider(tenantA)))
        {
            contextA.TestEntities.Add(new TestEntity(Guid.NewGuid(), "Entidad-A", 10) { TenantId = tenantA });
            await contextA.SaveChangesAsync();
        }

        using (var contextB = new MultiTenantTestDbContext(_options, new FakeTenantProvider(tenantB)))
        {
            contextB.TestEntities.Add(new TestEntity(Guid.NewGuid(), "Entidad-B", 20) { TenantId = tenantB });
            await contextB.SaveChangesAsync();
        }

        using (var readAsTenantA = new MultiTenantTestDbContext(_options, new FakeTenantProvider(tenantA)))
        {
            var visible = await readAsTenantA.TestEntities.ToListAsync();
            visible.Should().ContainSingle(e => e.Name == "Entidad-A");
            visible.Should().NotContain(e => e.Name == "Entidad-B");
        }

        using (var readAsTenantB = new MultiTenantTestDbContext(_options, new FakeTenantProvider(tenantB)))
        {
            var visible = await readAsTenantB.TestEntities.ToListAsync();
            visible.Should().ContainSingle(e => e.Name == "Entidad-B");
            visible.Should().NotContain(e => e.Name == "Entidad-A");
        }
    }

    [Fact]
    public async Task QueryFilter_WithMultiTenancyDisabled_SeesAllTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var contextA = new MultiTenantTestDbContext(_options, new FakeTenantProvider(tenantA)))
        {
            contextA.TestEntities.Add(new TestEntity(Guid.NewGuid(), "Entidad-A", 10) { TenantId = tenantA });
            await contextA.SaveChangesAsync();
        }

        using (var contextB = new MultiTenantTestDbContext(_options, new FakeTenantProvider(tenantB)))
        {
            contextB.TestEntities.Add(new TestEntity(Guid.NewGuid(), "Entidad-B", 20) { TenantId = tenantB });
            await contextB.SaveChangesAsync();
        }

        using var readWithoutTenancy = new MultiTenantTestDbContext(_options, new FakeTenantProvider(null, isMultiTenancyEnabled: false));
        var visible = await readWithoutTenancy.TestEntities.ToListAsync();

        visible.Should().HaveCount(2);
    }

    [Fact]
    public async Task QueryFilter_CombinesTenantIsolationWithSoftDelete()
    {
        var tenantA = Guid.NewGuid();
        Guid deletedId;

        using (var contextA = new MultiTenantTestDbContext(_options, new FakeTenantProvider(tenantA)))
        {
            var entity = new TestEntity(Guid.NewGuid(), "Entidad-Borrada", 10) { TenantId = tenantA };
            deletedId = entity.Id;
            contextA.TestEntities.Add(entity);
            await contextA.SaveChangesAsync();

            // Simula el resultado del SoftDeleteInterceptor (Tarea 1.5) sin registrarlo aquí:
            // el filtro global debe ocultar el registro por IsDeleted aunque siga existiendo físicamente.
            entity.IsDeleted = true;
            contextA.TestEntities.Update(entity);
            await contextA.SaveChangesAsync();
        }

        using var readAsTenantA = new MultiTenantTestDbContext(_options, new FakeTenantProvider(tenantA));
        var visible = await readAsTenantA.TestEntities.ToListAsync();

        visible.Should().NotContain(e => e.Id == deletedId);
    }

    public void Dispose() => _connection.Dispose();
}
