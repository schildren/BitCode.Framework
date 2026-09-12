using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F1-19 — evidencia de por qué el framework NO activa <c>AddDbContextPool</c>/
/// <c>PooledDbContextFactory</c> para <c>MultiTenantDbContext</c> (ver
/// <c>docs/adr/0012-dbcontext-pooling-no-adoptado.md</c>).
///
/// Hipótesis original de esta tarea: <c>MultiTenantDbContext</c> resuelve <c>TenantId</c>/
/// <c>IsMultiTenancyEnabled</c> UNA SOLA VEZ en su constructor (campos <c>readonly</c>,
/// <c>MultiTenantDbContext.cs:10-17</c>) y los cierra sobre el filtro global de EF Core en
/// <c>OnModelCreating</c>. Como el pooling de EF Core reutiliza la MISMA instancia de <c>DbContext</c>
/// entre scopes distintos sin volver a ejecutar el constructor de la clase derivada, un contexto
/// reciclado del pool seguiría filtrando por el tenant que construyó esa instancia por primera vez,
/// nunca el del scope que lo recibe después — contaminación de estado entre tenants.
///
/// Resultado real (más contundente que la hipótesis): EF Core ni siquiera permite intentarlo. Cuando
/// pooling está activo, EF Core prohíbe en tiempo de ejecución que <c>OnConfiguring</c> modifique
/// <c>DbContextOptions</c> (lo detecta la primera vez que se accede a los servicios internos del
/// contexto, p. ej. al llamar <c>Database.EnsureCreatedAsync</c> o ejecutar cualquier query), y
/// <c>MultiTenantDbContext.OnConfiguring</c> hace exactamente eso: llama
/// <c>optionsBuilder.ReplaceService&lt;IModelCacheKeyFactory, PerInstanceModelCacheKeyFactory&gt;()</c>
/// (el mecanismo que F1-11 introdujo para evitar que EF Core cachee el modelo — y con él el filtro de
/// tenant cerrado por closure — entre instancias). Este test demuestra, contra SQL Server real, que
/// intentar registrar <c>MultiTenantTestDbContext</c> (código de producción sin modificar) con
/// <c>AddDbContextPool</c> falla de inmediato con <see cref="InvalidOperationException"/>, antes de que
/// pueda ejecutarse ninguna consulta — es decir, hoy el framework no solo "no debería" activar pooling
/// por el riesgo de contaminación de estado entre tenants: literalmente no puede, mientras
/// <c>PerInstanceModelCacheKeyFactory</c> siga siendo necesario (ver ADR 0012 para el detalle completo
/// de por qué quitarlo para habilitar pooling sería aún más riesgoso que no tener pooling).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class DbContextPoolingEvaluationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString(
        [System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("BitCodeFrameworkPooling", testName);

    [Fact]
    public async Task AddDbContextPool_WithMultiTenantDbContext_FailsFast_BecauseOnConfiguringReplacesModelCacheKeyFactory()
    {
        var connectionString = BuildIsolatedConnectionString();

        var services = new ServiceCollection();
        services.AddScoped<ITenantProvider>(_ => new FakeTenantProvider(Guid.NewGuid()));
        services.AddDbContextPool<MultiTenantTestDbContext>(
            options => options.UseSqlServer(connectionString),
            poolSize: 1);
        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();

        // No hace falta llegar a ejecutar una query ni a simular un segundo tenant: EF Core corta el
        // camino en el primer acceso a los servicios internos del contexto, apenas se activa pooling
        // sobre un DbContext cuyo OnConfiguring reemplaza un servicio (PerInstanceModelCacheKeyFactory).
        var act = async () => await context.Database.EnsureCreatedAsync();

        await act.Should().ThrowAsync<InvalidOperationException>(
                "EF Core prohíbe explícitamente que OnConfiguring modifique DbContextOptions cuando " +
                "el pooling está activo; MultiTenantDbContext.OnConfiguring reemplaza " +
                "IModelCacheKeyFactory por PerInstanceModelCacheKeyFactory (mecanismo introducido por " +
                "F1-11 para evitar que el filtro de tenant quede congelado en el modelo cacheado de " +
                "EF Core), así que hoy AddDbContextPool/PooledDbContextFactory no es solo riesgoso " +
                "para la tenancy: literalmente no funciona sobre MultiTenantDbContext")
            .WithMessage("*OnConfiguring*pooling*");
    }
}
