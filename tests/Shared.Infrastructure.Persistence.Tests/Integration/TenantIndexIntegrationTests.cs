using System.Data;
using System.Diagnostics;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F1-20 — evidencia real (plan de ejecución + tiempos) del criterio de aceptación "planes sin
/// regresiones críticas" para la convención automática de índice por defecto que agrega
/// <c>TenantIndexModelConfigurator</c> a toda entidad <see cref="BitCode.Framework.Shared.Kernel.ITenantEntity"/>
/// (ver <c>src/Shared.Infrastructure.Persistence/MultiTenancy/TenantIndexModelConfigurator.cs</c>).
///
/// Compara, contra SQL Server real (Testcontainers, no SQLite in-memory — el optimizador de consultas
/// de SQLite no representa fielmente el de SQL Server para decidir entre scan y seek), la misma
/// consulta (<c>SELECT COUNT(*) FROM TestEntities WHERE TenantId = @tenantId AND IsDeleted = 0</c>,
/// el predicado exacto que el filtro global de EF Core aplica a toda consulta contra una entidad
/// <c>ITenantEntity</c> + <c>ISoftDelete</c>) sobre dos esquemas con el mismo volumen de datos:
///
/// - "Antes": tabla creada por <see cref="TestDbContext"/> (DbContext plano, sin
///   <c>TenantIndexModelConfigurator</c>) — solo tiene índice de clustered index sobre la PK (Id).
/// - "Después": tabla creada por <see cref="MultiTenantTestDbContext"/> (código de producción real,
///   hereda de <c>MultiTenantDbContext</c>) — <c>TenantIndexModelConfigurator</c> agrega
///   automáticamente el índice compuesto <c>(TenantId, IsDeleted)</c> porque <c>TestEntity</c>
///   implementa ambas interfaces.
///
/// Mecanismo de captura del plan real: <c>SET STATISTICS XML ON</c> (plan de ejecución real, no
/// estimado) leído como el segundo resultset de un <c>SqlDataReader</c> — se elige este mecanismo en
/// vez de <c>sys.dm_exec_query_plan</c> porque no depende de que el plan siga en el caché de planes de
/// SQL Server en el momento de la consulta, y porque refleja el plan de la ejecución real que se está
/// midiendo, no una consulta posterior separada al caché.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class TenantIndexIntegrationTests
{
    private const int TenantCount = 20;
    private const int RowsPerTenant = 1_000;
    private const int TotalRows = TenantCount * RowsPerTenant;
    private const int TimingIterations = 20;

    private readonly SqlServerContainerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TenantIndexIntegrationTests(SqlServerContainerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string BuildIsolatedConnectionString(string suffix) =>
        _fixture.BuildIsolatedConnectionString("TenantIx", suffix);

    [Fact]
    public async Task TenantFilteredQuery_WithAutomaticTenantIndex_AvoidsFullScan_AndIsFasterThanWithoutIndex()
    {
        var beforeConnectionString = BuildIsolatedConnectionString("Before");
        var afterConnectionString = BuildIsolatedConnectionString("After");

        // Esquema "antes": TestDbContext es un DbContext plano (sin MultiTenantDbContext, sin
        // TenantIndexModelConfigurator) — la tabla TestEntities solo tiene la PK como índice.
        var beforeOptions = new DbContextOptionsBuilder<TestDbContext>().UseSqlServer(beforeConnectionString).Options;
        await using (var beforeContext = new TestDbContext(beforeOptions))
        {
            await beforeContext.Database.EnsureCreatedAsync();
        }

        // Esquema "después": MultiTenantTestDbContext es código de producción real; al implementar
        // TestEntity ITenantEntity + ISoftDelete, TenantIndexModelConfigurator agrega automáticamente
        // el índice compuesto (TenantId, IsDeleted) durante OnModelCreating.
        var afterOptions = new DbContextOptionsBuilder<MultiTenantTestDbContext>().UseSqlServer(afterConnectionString).Options;
        await using (var afterContext = new MultiTenantTestDbContext(afterOptions, new FakeTenantProvider(Guid.NewGuid())))
        {
            await afterContext.Database.EnsureCreatedAsync();
        }

        var tenantIds = Enumerable.Range(0, TenantCount).Select(_ => Guid.NewGuid()).ToArray();
        var targetTenantId = tenantIds[0];

        await SeedAsync(beforeConnectionString, tenantIds);
        await SeedAsync(afterConnectionString, tenantIds);

        var (beforePlanXml, beforeCount) = await CapturePlanAsync(beforeConnectionString, targetTenantId);
        var (afterPlanXml, afterCount) = await CapturePlanAsync(afterConnectionString, targetTenantId);

        // Control de sanidad: ambas consultas deben coincidir en el resultado (misma cantidad de filas
        // del tenant elegido) — si no coincidieran, la comparación de planes/tiempos no sería válida
        // (estaríamos comparando conjuntos de datos distintos, no el efecto del índice).
        beforeCount.Should().Be(RowsPerTenant);
        afterCount.Should().Be(RowsPerTenant);

        _output.WriteLine($"TotalRows={TotalRows}, TenantCount={TenantCount}, RowsPerTenant={RowsPerTenant}");
        _output.WriteLine("Plan SIN índice de tenant (contiene 'Clustered Index Scan'): " + beforePlanXml.Contains("Clustered Index Scan"));
        _output.WriteLine("Plan CON índice de tenant (contiene 'Index Seek'): " + afterPlanXml.Contains("Index Seek"));

        beforePlanXml.Should().Contain(
            "Clustered Index Scan",
            "sin un índice que arranque por TenantId, SQL Server no tiene otra forma de resolver " +
            "WHERE TenantId = @p0 AND IsDeleted = 0 que no sea recorrer toda la tabla (todos los " +
            "tenants), independientemente de cuántas filas pertenezcan al tenant consultado");

        afterPlanXml.Should().Contain(
            "Index Seek",
            "con el índice compuesto (TenantId, IsDeleted) que agrega TenantIndexModelConfigurator, " +
            "SQL Server puede resolver el mismo filtro con un seek dirigido a las filas del tenant " +
            "consultado, sin depender del volumen total de la tabla");

        afterPlanXml.Should().NotContain(
            "Clustered Index Scan",
            "el plan CON índice no debería necesitar recorrer la tabla completa para resolver el " +
            "mismo filtro que el plan SIN índice sí resuelve con un scan completo");

        // Tiempos reales como evidencia complementaria del plan (no como único criterio): se miden por
        // separado, sin SET STATISTICS XML activo (que añade su propio overhead de captura), con
        // warm-up previo para no medir la primera compilación de plan de cada conexión.
        var beforeElapsed = await MeasureAsync(beforeConnectionString, targetTenantId, TimingIterations);
        var afterElapsed = await MeasureAsync(afterConnectionString, targetTenantId, TimingIterations);

        var beforeAvgMs = beforeElapsed.TotalMilliseconds / TimingIterations;
        var afterAvgMs = afterElapsed.TotalMilliseconds / TimingIterations;

        _output.WriteLine(
            $"SIN índice: {beforeElapsed.TotalMilliseconds:F2} ms total, {beforeAvgMs:F4} ms/iteración | " +
            $"CON índice: {afterElapsed.TotalMilliseconds:F2} ms total, {afterAvgMs:F4} ms/iteración | " +
            $"Speedup: {(beforeAvgMs / afterAvgMs):F1}x");

        // No se fija un ratio exacto (SQL Server en un contenedor de Testcontainers introduce
        // variabilidad de E/S/CPU compartida del entorno de CI/desarrollo) — el invariante que sí es
        // razonable exigir, dado el volumen de esta prueba (20.000 filas, filtro que deja el 5%), es
        // que un seek dirigido sea más rápido que un scan completo de la tabla.
        afterAvgMs.Should().BeLessThan(beforeAvgMs);
    }

    private static async Task SeedAsync(string connectionString, IReadOnlyList<Guid> tenantIds)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Amount", typeof(int));
        table.Columns.Add("TenantId", typeof(Guid));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        table.Columns.Add("CreatedBy", typeof(string));
        table.Columns.Add("ModifiedAtUtc", typeof(DateTime));
        table.Columns.Add("ModifiedBy", typeof(string));
        table.Columns.Add("IsDeleted", typeof(bool));
        table.Columns.Add("DeletedAtUtc", typeof(DateTime));
        table.Columns.Add("DeletedBy", typeof(string));

        var random = new Random(Seed: 42);
        foreach (var tenantId in tenantIds)
        {
            for (var i = 0; i < RowsPerTenant; i++)
            {
                var row = table.NewRow();
                row["Id"] = Guid.NewGuid();
                row["Name"] = $"Entity-{i}";
                row["Amount"] = random.Next(0, 5_000);
                row["TenantId"] = tenantId;
                row["CreatedAtUtc"] = DateTime.UtcNow;
                row["CreatedBy"] = DBNull.Value;
                row["ModifiedAtUtc"] = DBNull.Value;
                row["ModifiedBy"] = DBNull.Value;
                row["IsDeleted"] = false;
                row["DeletedAtUtc"] = DBNull.Value;
                row["DeletedBy"] = DBNull.Value;
                table.Rows.Add(row);
            }
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var bulkCopy = new SqlBulkCopy(connection) { DestinationTableName = "TestEntities", BatchSize = 5_000 };
        foreach (DataColumn column in table.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(table);
    }

    private static async Task<(string PlanXml, int Count)> CapturePlanAsync(string connectionString, Guid tenantId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var setOn = connection.CreateCommand())
        {
            setOn.CommandText = "SET STATISTICS XML ON;";
            await setOn.ExecuteNonQueryAsync();
        }

        var planXml = string.Empty;
        var count = 0;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM TestEntities WHERE TenantId = @tenantId AND IsDeleted = 0;";
            command.Parameters.AddWithValue("@tenantId", tenantId);

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                count = reader.GetInt32(0);
            }

            if (await reader.NextResultAsync() && await reader.ReadAsync())
            {
                planXml = reader.GetString(0);
            }
        }

        await using (var setOff = connection.CreateCommand())
        {
            setOff.CommandText = "SET STATISTICS XML OFF;";
            await setOff.ExecuteNonQueryAsync();
        }

        return (planXml, count);
    }

    private static async Task<TimeSpan> MeasureAsync(string connectionString, Guid tenantId, int iterations)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Warm-up: evita medir la primera compilación de plan de esta conexión.
        await ExecuteCountAsync(connection, tenantId);

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            await ExecuteCountAsync(connection, tenantId);
        }

        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private static async Task ExecuteCountAsync(SqlConnection connection, Guid tenantId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM TestEntities WHERE TenantId = @tenantId AND IsDeleted = 0;";
        command.Parameters.AddWithValue("@tenantId", tenantId);
        _ = await command.ExecuteScalarAsync();
    }
}
