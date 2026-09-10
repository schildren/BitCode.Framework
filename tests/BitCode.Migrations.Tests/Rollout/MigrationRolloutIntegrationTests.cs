using BitCode.Framework.Shared.Testing;
using BitCode.Framework.Tests.Migrations.TestModel;
using BitCode.Framework.Tools.Migrations.Core;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BitCode.Framework.Tests.Migrations.Rollout;

public class MigrationRolloutIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _sqlServerFixture.DisposeAsync();
    }

    private DbContextOptions<SampleMigrationDbContext> CreateOptions(string connectionString)
    {
        return new DbContextOptionsBuilder<SampleMigrationDbContext>()
            .UseSqlServer(connectionString, b =>
            {
                b.MigrationsAssembly(typeof(SampleMigrationDbContext).Assembly.GetName().Name);
            })
            .Options;
    }

    [Fact]
    public async Task FullLifecycle_Forward_Expand_And_Rollback_SucceedsWithDataIntegrity()
    {
        var connectionString = _sqlServerFixture.BuildIsolatedConnectionString("MigrationLifecycle", Guid.NewGuid().ToString("N"));
        var options = CreateOptions(connectionString);

        // =====================================================================
        // PASO 1: Estado inicial sobre base limpia
        // =====================================================================
        await using (var context = new SampleMigrationDbContext(options))
        {
            var runner = new MigrationRunner(context);
            var initialStatus = await runner.GetStatusAsync();

            initialStatus.AppliedMigrations.Should().BeEmpty();
            initialStatus.PendingMigrations.Should().HaveCount(2);
            initialStatus.HasPendingMigrations.Should().BeTrue();
        }

        // =====================================================================
        // PASO 2: Forward Rollout hasta V1 (InitialMigration)
        // =====================================================================
        var entityId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await using (var context = new SampleMigrationDbContext(options))
        {
            var runner = new MigrationRunner(context);
            var applied = await runner.MigrateForwardAsync("20260910000001_InitialMigration");

            applied.Should().ContainSingle().Which.Should().Be("20260910000001_InitialMigration");

            var status = await runner.GetStatusAsync();
            status.AppliedMigrations.Should().ContainSingle().Which.Should().Be("20260910000001_InitialMigration");
            status.PendingMigrations.Should().ContainSingle().Which.Should().Be("20260910000002_ExpandMigration");

            // Insertar datos en esquema V1 usando SQL directo para evitar depender de columnas de V2
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = "INSERT INTO SampleEntities (Id, TenantId, Nombre, CreatedAtUtc) VALUES (@id, @tenant, @nombre, @created)";
            insertCmd.Parameters.AddWithValue("@id", entityId);
            insertCmd.Parameters.AddWithValue("@tenant", tenantId);
            insertCmd.Parameters.AddWithValue("@nombre", "Entidad En V1");
            insertCmd.Parameters.AddWithValue("@created", DateTime.UtcNow);
            var rowsInserted = await insertCmd.ExecuteNonQueryAsync();
            rowsInserted.Should().Be(1);
        }

        // =====================================================================
        // PASO 3: Forward Rollout a V2 (ExpandMigration) con datos existentes
        // =====================================================================
        await using (var context = new SampleMigrationDbContext(options))
        {
            var runner = new MigrationRunner(context);
            var applied = await runner.MigrateForwardAsync("20260910000002_ExpandMigration");

            applied.Should().ContainSingle().Which.Should().Be("20260910000002_ExpandMigration");

            var status = await runner.GetStatusAsync();
            status.AppliedMigrations.Should().HaveCount(2);
            status.PendingMigrations.Should().BeEmpty();
            status.HasPendingMigrations.Should().BeFalse();

            // Verificar que los datos insertados en V1 sobreviven con los valores por defecto de V2
            var loaded = await context.SampleEntities.FirstOrDefaultAsync(e => e.Id == entityId);
            loaded.Should().NotBeNull();
            loaded!.Nombre.Should().Be("Entidad En V1");
            loaded.Descripcion.Should().BeNull(); // Nullable en Expand
            loaded.Activo.Should().BeTrue();     // Default value en Expand

            // Insertar nueva fila aprovechando el esquema expandido
            var entityV2 = new SampleEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Nombre = "Entidad En V2",
                CreatedAtUtc = DateTime.UtcNow,
                Descripcion = "Creado en V2 expandido",
                Activo = true
            };
            context.SampleEntities.Add(entityV2);
            await context.SaveChangesAsync();
        }

        // =====================================================================
        // PASO 4: Rollback ensayado hacia V1 (InitialMigration)
        // =====================================================================
        await using (var context = new SampleMigrationDbContext(options))
        {
            var runner = new MigrationRunner(context);
            var rolledBack = await runner.RollbackAsync("20260910000001_InitialMigration");

            rolledBack.Should().ContainSingle().Which.Should().Be("20260910000002_ExpandMigration");

            var status = await runner.GetStatusAsync();
            status.AppliedMigrations.Should().ContainSingle().Which.Should().Be("20260910000001_InitialMigration");
            status.PendingMigrations.Should().ContainSingle().Which.Should().Be("20260910000002_ExpandMigration");

            // Verificar vía SQL directo que las columnas de V2 ya no existen, pero los datos de V1 siguen presentes
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT COUNT(*) FROM SampleEntities WHERE Id = @id";
            selectCmd.Parameters.AddWithValue("@id", entityId);
            var count = (int)(await selectCmd.ExecuteScalarAsync())!;
            count.Should().Be(1);

            // Verificar que intentar consultar columna 'Descripcion' o 'Activo' falla porque fueron revertidas
            await using var invalidQueryCmd = connection.CreateCommand();
            invalidQueryCmd.CommandText = "SELECT Descripcion FROM SampleEntities WHERE Id = @id";
            invalidQueryCmd.Parameters.AddWithValue("@id", entityId);
            var act = async () => await invalidQueryCmd.ExecuteScalarAsync();
            await act.Should().ThrowAsync<SqlException>()
                .WithMessage("*Invalid column name 'Descripcion'*");
        }
    }

    [Fact]
    public async Task ScriptGenerator_ProducesIdempotentSql()
    {
        var connectionString = _sqlServerFixture.BuildIsolatedConnectionString("MigrationScript", Guid.NewGuid().ToString("N"));
        var options = CreateOptions(connectionString);

        await using var context = new SampleMigrationDbContext(options);
        var generator = new MigrationScriptGenerator(context);
        var script = generator.GenerateIdempotentScript();

        script.Should().NotBeNullOrWhiteSpace();
        script.Should().Contain("__EFMigrationsHistory");
        script.Should().Contain("SampleEntities");
        script.Should().Contain("20260910000001_InitialMigration");
        script.Should().Contain("20260910000002_ExpandMigration");
    }
}
