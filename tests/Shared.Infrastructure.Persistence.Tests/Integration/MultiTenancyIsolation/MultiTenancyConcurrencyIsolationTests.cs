using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration.MultiTenancyIsolation;

/// <summary>
/// F1-16 — todas las pruebas de aislamiento anteriores (F1-12, y las negativas de esta misma tarea en
/// <see cref="MultiTenancyNegativeIsolationTests"/>) ejercitan un tenant por vez, de forma secuencial.
/// El criterio de aceptación "cero fuga entre tenants" también debe sostenerse bajo carga concurrente
/// real: varios tenants distintos ejecutando lecturas y escrituras al mismo tiempo, contra la misma
/// base de datos compartida (mismo mecanismo que produciría una fuga si el filtro global dependiera de
/// algún estado compartido/estático en vez de resolverse por scope de DI, como
/// <c>ITenantProvider</c>/<c>ITenantContext</c> lo hacen).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiTenancyConcurrencyIsolationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("BitCodeFramework", testName);

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString, ITenantProvider tenantProvider)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => tenantProvider);
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>().Database.EnsureCreatedAsync();

        return provider;
    }

    /// <summary>
    /// N tenants insertan y luego leen su propio listado completo, todo en paralelo real
    /// (<c>Task.WhenAll</c>), cada uno con su propio <see cref="ServiceProvider"/>/scope/DbContext
    /// (tal como serían N requests HTTP concurrentes contra la misma instancia de la API, cada uno
    /// resolviendo su propio <c>ITenantProvider</c> desde su propio JWT). Ninguno debe ver, ni una
    /// sola vez, una fila de otro tenant.
    /// </summary>
    [Fact]
    public async Task ConcurrentWritesAndReads_AcrossMultipleTenants_NeverCrossContaminate()
    {
        var connectionString = BuildIsolatedConnectionString();
        const int tenantCount = 8;
        var tenantIds = Enumerable.Range(0, tenantCount).Select(_ => Guid.NewGuid()).ToArray();

        // Prepara el esquema una única vez antes de la carga concurrente (EnsureCreatedAsync no es
        // seguro de invocar en paralelo múltiples veces sobre una base recién creada).
        await using (var setupProvider = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantIds[0])))
        {
            // El using de BuildProviderAsync ya llama EnsureCreatedAsync.
        }

        var operations = tenantIds.Select(async (tenantId, index) =>
        {
            await using var provider = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantId));
            await using var scope = provider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var ownEntityName = $"Tenant-{index}-Entidad";
            await repository.AddAsync(new TestEntity(Guid.NewGuid(), ownEntityName, index) { TenantId = tenantId });
            await unitOfWork.SaveChangesAsync();

            // Pequeña ventana para maximizar la probabilidad de solaparse en el tiempo con las
            // escrituras de los demás tenants antes de leer.
            await Task.Delay(Random.Shared.Next(5, 30));

            var visible = await repository.ListAsync();
            return (tenantId, ownEntityName, visible);
        });

        var results = await Task.WhenAll(operations);

        foreach (var (tenantId, ownEntityName, visible) in results)
        {
            visible.Should().ContainSingle(e => e.Name == ownEntityName,
                $"tenant {tenantId} debe ver exactamente su propia entidad");
            visible.Should().OnlyContain(e => e.TenantId == tenantId,
                $"tenant {tenantId} nunca debe ver una fila con TenantId distinto al propio, incluso bajo escritura/lectura concurrente de otros {tenantCount - 1} tenants");
        }
    }

    /// <summary>
    /// Variante enfocada en el acceso puntual por Id (F1-12) bajo concurrencia: cada tenant intenta,
    /// en paralelo, leer por Id tanto su propia entidad como la de todos los demás tenants. Ningún
    /// intento cruzado debe tener éxito, sin importar el orden de ejecución real que decida el
    /// scheduler o SQL Server.
    /// </summary>
    [Fact]
    public async Task ConcurrentGetByIdAttempts_AcrossTenants_NeverReturnAnotherTenantsEntity()
    {
        var connectionString = BuildIsolatedConnectionString();
        const int tenantCount = 5;
        var tenantIds = Enumerable.Range(0, tenantCount).Select(_ => Guid.NewGuid()).ToArray();
        var entityIdsByTenant = tenantIds.ToDictionary(t => t, _ => Guid.NewGuid());

        await using (var setupProvider = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantIds[0])))
        {
        }

        // Siembra: cada tenant crea su propia entidad, secuencialmente (evita medir contención de
        // escritura, que no es el objetivo de esta prueba).
        foreach (var tenantId in tenantIds)
        {
            await using var provider = await BuildProviderAsync(connectionString, new FakeTenantProvider(tenantId));
            await using var scope = provider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repository.AddAsync(new TestEntity(entityIdsByTenant[tenantId], $"Secreto-{tenantId:N}", 1) { TenantId = tenantId });
            await unitOfWork.SaveChangesAsync();
        }

        // Cada tenant, en paralelo, intenta leer por Id la entidad de TODOS los tenants (incluida la
        // propia). Solo el intento sobre su propio Id debe tener éxito.
        var attempts = tenantIds.SelectMany(readerTenantId =>
            tenantIds.Select(async targetTenantId =>
            {
                await using var provider = await BuildProviderAsync(connectionString, new FakeTenantProvider(readerTenantId));
                await using var scope = provider.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestEntity, Guid>>();
                var found = await repository.GetByIdAsync(entityIdsByTenant[targetTenantId]);
                return (readerTenantId, targetTenantId, found);
            }));

        var results = await Task.WhenAll(attempts);

        foreach (var (readerTenantId, targetTenantId, found) in results)
        {
            if (readerTenantId == targetTenantId)
            {
                found.Should().NotBeNull($"tenant {readerTenantId} debe poder leer su propia entidad");
                found!.TenantId.Should().Be(readerTenantId);
            }
            else
            {
                found.Should().BeNull($"tenant {readerTenantId} nunca debe poder leer por Id la entidad de tenant {targetTenantId}, ni siquiera bajo acceso concurrente de {tenantCount} tenants distintos");
            }
        }
    }
}
