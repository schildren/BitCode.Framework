using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Tests.Migrations.TestModel;

public class SampleEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string? Descripcion { get; set; }
    public bool Activo { get; set; }
}

public class SampleMigrationDbContext : DbContext
{
    public SampleMigrationDbContext(DbContextOptions<SampleMigrationDbContext> options)
        : base(options)
    {
    }

    public DbSet<SampleEntity> SampleEntities => Set<SampleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SampleEntity>(builder =>
        {
            builder.ToTable("SampleEntities");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Nombre).HasMaxLength(100).IsRequired();
            builder.Property(e => e.Descripcion).HasMaxLength(500).IsRequired(false);
            builder.Property(e => e.Activo).HasDefaultValue(true);
        });
    }
}
