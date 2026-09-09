using Microsoft.EntityFrameworkCore;

namespace Sample.ImportExport.Api.Clientes;

/// <summary>
/// Persistencia propia del host para <see cref="Cliente"/> -- deliberadamente un
/// <see cref="DbContext"/> simple, SIN <c>MultiTenantDbContext</c>/Outbox/idempotencia (esas
/// preocupaciones son del framework, no de este modelo de demostración). <see cref="Cliente.TenantId"/>
/// se filtra manualmente en <see cref="ClientesExportDataSource"/> -- ver el <c>remarks</c> de
/// <see cref="Cliente"/>.
/// </summary>
public sealed class SampleClientesDbContext(DbContextOptions<SampleClientesDbContext> options) : DbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Cliente>(builder =>
        {
            builder.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
        });
    }
}
