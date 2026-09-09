using System.Diagnostics;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit.Abstractions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F5-13 (Fase 5 — Disaster Recovery y multi-región, <c>docs/chaos-regional-fase5.md</c>), escenario 3
/// "Caída de una región completa": <c>Shared.Infrastructure.Persistence</c> (SQL Server) es, según el
/// BIA de Fase 5 (<c>docs/bia-fase5.md</c> fila 4), el propietario de escritura single-writer de una
/// región (perfil Platinum objetivo) -- si su instancia de SQL Server desaparece por completo (la región
/// entera cae, no solo una réplica de aplicación), cualquier comando regional que esta instancia
/// intentara ejecutar (tras haber pasado ya <c>RegionalOwnershipBehavior</c>, F5-02, que confirmó que
/// ESTA es la región propietaria) debe fallar de forma ACOTADA y CONTROLADA -- nunca colgarse
/// indefinidamente esperando una base de datos que ya no existe.
/// </summary>
/// <remarks>
/// Usa una instancia PROPIA de SQL Server (<see cref="MsSqlContainer"/> real, no
/// <see cref="SqlServerContainerFixture"/> compartida) por el mismo motivo que
/// <c>SqlLogShippingRpoIntegrationTests</c>/<c>SqlBackupRestoreIntegrationTests</c> (F5-04/F5-07): esta
/// prueba necesita DETENER el servidor a mitad de la ejecución, algo que rompería cualquier otra prueba
/// que comparta la instancia vía la colección compartida.
/// <para/>
/// No reconstruye el stack completo de EF Core/<c>AddSharedPersistence</c> (innecesario para lo que mide
/// esta prueba): usa <see cref="SqlConnection"/>/<see cref="SqlCommand"/> directamente, con
/// <c>CommandTimeout</c> configurado -- el mismo mecanismo subyacente (<c>Microsoft.Data.SqlClient</c>)
/// que usa EF Core internamente para aplicar <c>PersistenceOptions.CommandTimeoutSeconds</c> (F1-10,
/// <c>docs/convenciones.md</c> regla dura 11). La propiedad que se mide -- que un comando SQL individual
/// no puede colgarse más allá de un límite configurado, independientemente de si el servidor de destino
/// ya no existe -- es exactamente la misma en ambos casos.
/// </remarks>
public sealed class RegionOutageBoundedFailureIntegrationTests : IAsyncLifetime
{
    private const string DatabaseName = "RegionOutageDemo";

    // SLO documentado en docs/chaos-regional-fase5.md, escenario 3: un comando de escritura contra el
    // SQL Server de la región propietaria, con esa región completamente caída, debe fallar dentro de
    // este límite -- acotado explícitamente por el timeout de comando configurado (análogo a
    // PersistenceOptions.CommandTimeoutSeconds, F1-10), nunca por un cuelgue sin límite.
    private const int CommandTimeoutSeconds = 3;
    private static readonly TimeSpan BoundedFailureSlo = TimeSpan.FromSeconds(CommandTimeoutSeconds + 5);

    private readonly MsSqlContainer _regionSqlServer = new MsSqlBuilder().Build();
    private readonly ITestOutputHelper _output;

    public RegionOutageBoundedFailureIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync() => await _regionSqlServer.StartAsync();

    public async Task DisposeAsync() => await _regionSqlServer.DisposeAsync();

    [Fact]
    public async Task RegionSqlServerCaidaPorCompleto_ComandoFallaAcotadoYControlado_SinColgarseIndefinidamente()
    {
        var connectionString = BuildConnectionString();

        // 1) Con la región (SQL Server) sana: confirma el camino feliz -- crea la base de datos de la
        //    región, una tabla de negocio de ejemplo (mismo rol conceptual que cualquier agregado
        //    persistido por Shared.Infrastructure.Persistence) y una escritura real exitosa.
        await using (var masterConnection = new SqlConnection(BuildConnectionString("master")))
        {
            await masterConnection.OpenAsync();
            await using var createDatabase = masterConnection.CreateCommand();
            createDatabase.CommandText = $"CREATE DATABASE [{DatabaseName}]";
            await createDatabase.ExecuteNonQueryAsync();
        }

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var createTable = connection.CreateCommand();
            createTable.CommandText =
                "CREATE TABLE dbo.PedidoRegional (Id INT IDENTITY(1,1) PRIMARY KEY, Payload NVARCHAR(50) NOT NULL)";
            await createTable.ExecuteNonQueryAsync();

            await using var insertSano = connection.CreateCommand();
            insertSano.CommandText = "INSERT INTO dbo.PedidoRegional (Payload) VALUES ('antes-de-la-caida')";
            var filas = await insertSano.ExecuteNonQueryAsync();
            filas.Should().Be(1, "la región propietaria debe poder escribir con normalidad antes del incidente");
        }

        // 2) Caída TOTAL de la región -- se detiene el contenedor real de SQL Server (no se simula con
        //    un mock ni se cierra solo la conexión del cliente): el "single writer" de la región deja de
        //    existir por completo, exactamente el escenario "región completa caída" de F5-13.
        await _regionSqlServer.StopAsync();

        // 3) Un comando de escritura posterior (equivalente a un ITransactionalCommand/ICommand simple
        //    que ya pasó RegionalOwnershipBehavior, F5-02, confiando en que esta es la región propietaria)
        //    debe fallar de forma acotada -- nunca colgarse indefinidamente. Se usa una conexión NUEVA
        //    (mismo patrón que un pool reciclado tras la caída) con ConnectTimeout/CommandTimeout cortos,
        //    análogos a PersistenceOptions.CommandTimeoutSeconds (F1-10).
        var connectionStringConTimeoutCorto = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = CommandTimeoutSeconds,
            Pooling = false,
        }.ConnectionString;

        var stopwatch = Stopwatch.StartNew();
        Exception? excepcionObservada = null;
        try
        {
            await using var connection = new SqlConnection(connectionStringConTimeoutCorto);
            await connection.OpenAsync();

            await using var insertTrasCaida = connection.CreateCommand();
            insertTrasCaida.CommandTimeout = CommandTimeoutSeconds;
            insertTrasCaida.CommandText = "INSERT INTO dbo.PedidoRegional (Payload) VALUES ('tras-la-caida-de-la-region')";
            await insertTrasCaida.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            excepcionObservada = ex;
        }
        stopwatch.Stop();

        var evidencia =
            $"Región SQL Server caída -- intento de escritura: excepción={excepcionObservada?.GetType().FullName ?? "ninguna (inesperado)"}, " +
            $"mensaje={excepcionObservada?.Message}, elapsed={stopwatch.Elapsed.TotalMilliseconds:F0}ms, " +
            $"SLO documentado (docs/chaos-regional-fase5.md, escenario 3): <= {BoundedFailureSlo.TotalSeconds}s";
        _output.WriteLine(evidencia);

        excepcionObservada.Should().NotBeNull(
            "una región completamente caída nunca debe dejar pasar una escritura como si hubiera " +
            "tenido éxito -- debe fallar de forma explícita y controlada" + Environment.NewLine + evidencia);
        stopwatch.Elapsed.Should().BeLessThanOrEqualTo(
            BoundedFailureSlo,
            "el fallo debe ser ACOTADO (gobernado por el timeout de conexión/comando configurado, " +
            "análogo a PersistenceOptions.CommandTimeoutSeconds de F1-10) -- un cuelgue indefinido " +
            "esperando una base de datos que ya no existe sería la violación real de F5-13" +
            Environment.NewLine + evidencia);
    }

    private string BuildConnectionString(string databaseName = DatabaseName)
    {
        var builder = new SqlConnectionStringBuilder(_regionSqlServer.GetConnectionString())
        {
            InitialCatalog = databaseName,
            Pooling = false,
        };

        return builder.ConnectionString;
    }
}
