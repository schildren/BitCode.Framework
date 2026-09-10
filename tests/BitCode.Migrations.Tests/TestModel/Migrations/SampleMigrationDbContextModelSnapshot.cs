using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace BitCode.Framework.Tests.Migrations.TestModel.Migrations;

[DbContext(typeof(SampleMigrationDbContext))]
partial class SampleMigrationDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 128);

        SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

        modelBuilder.Entity("BitCode.Framework.Tests.Migrations.TestModel.SampleEntity", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uniqueidentifier");

                b.Property<bool>("Activo")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("bit")
                    .HasDefaultValue(true);

                b.Property<DateTime>("CreatedAtUtc")
                    .HasColumnType("datetime2");

                b.Property<string>("Descripcion")
                    .HasMaxLength(500)
                    .HasColumnType("nvarchar(500)");

                b.Property<string>("Nombre")
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasColumnType("nvarchar(100)");

                b.Property<Guid>("TenantId")
                    .HasColumnType("uniqueidentifier");

                b.HasKey("Id");

                b.ToTable("SampleEntities", (string)null);
            });
#pragma warning restore 612, 618
    }
}
