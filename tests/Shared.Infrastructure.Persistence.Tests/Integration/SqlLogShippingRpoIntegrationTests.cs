using System.Text;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit.Abstractions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F5-04 (Fase 5 — Disaster Recovery y multi-región): mide, contra dos instancias reales de SQL
/// Server (dos contenedores Testcontainers independientes, sin filesystem compartido, exactamente
/// como serían dos regiones distintas), el RPO real y reproducible de un mecanismo de replicación
/// asíncrona por <c>log shipping</c> (backup completo inicial + backups de log periódicos aplicados
/// con <c>RESTORE LOG ... WITH STANDBY</c> sobre la "réplica"), bajo carga simulada.
/// </summary>
/// <remarks>
/// Ver <c>docs/replicacion-sql-fase5.md</c> sección 4 para el razonamiento completo de por qué se
/// eligió log shipping (mecanismo real, sin dependencias adicionales, demostrable en un solo host de
/// desarrollo) en vez de Always On Availability Groups (requiere Windows Server Failover Cluster /
/// Pacemaker + quórum multi-nodo, no disponible con contenedores Linux aislados) para ESTA prueba
/// ejecutable — el documento de arquitectura recomienda Always On AG (o Distributed AG) como
/// topología productiva.
///
/// No usa <see cref="SqlServerContainerFixture"/> ni la <c>SqlServerCollection</c> compartida: esta
/// prueba necesita, a propósito, DOS instancias de SQL Server completamente independientes (dos
/// contenedores, cada uno su propio disco), para que la copia del backup completo y de cada backup
/// de log entre "primaria" y "réplica" sea una copia real entre dos sistemas de archivos distintos —
/// igual que cruzaría una red entre regiones reales — y no una operación dentro del mismo contenedor.
/// </remarks>
public sealed class SqlLogShippingRpoIntegrationTests : IAsyncLifetime
{
    private const string DatabaseName = "RpoDemo";
    private const string BackupDirectory = "/tmp/logship";

    private readonly MsSqlContainer _primary = new MsSqlBuilder().Build();
    private readonly MsSqlContainer _replica = new MsSqlBuilder().Build();
    private readonly ITestOutputHelper _output;

    public SqlLogShippingRpoIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_primary.StartAsync(), _replica.StartAsync());

        // Directorio de backups con permisos abiertos: el proceso sqlservr (usuario "mssql" dentro
        // del contenedor) necesita poder escribir ahí, y el propio Testcontainers copia bytes al
        // contenedor como root por defecto — 777 evita que la prueba dependa de qué UID exacto usa
        // cada operación (lo único relevante para F5-04 es medir el RPO del mecanismo, no una
        // política de permisos de producción).
        await _primary.ExecAsync(["mkdir", "-p", BackupDirectory]);
        await _primary.ExecAsync(["chmod", "777", BackupDirectory]);
        await _replica.ExecAsync(["mkdir", "-p", BackupDirectory]);
        await _replica.ExecAsync(["chmod", "777", BackupDirectory]);
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(_primary.DisposeAsync().AsTask(), _replica.DisposeAsync().AsTask());
    }

    /// <summary>
    /// Criterio de aceptación de F5-04 ("RPO medido"): bajo carga simulada de escritura continua en
    /// la primaria, con backups de log aplicados periódicamente a la réplica (el mismo mecanismo de
    /// log shipping descrito en <c>docs/replicacion-sql-fase5.md</c>), el RPO real —la ventana de
    /// datos confirmados en la primaria que NO llegaron a la réplica en el momento de una caída
    /// simulada— es (a) mayor que cero (prueba que la replicación es asíncrona, no síncrona: hay
    /// pérdida posible, consistente con perfiles Standard/Gold del BIA, nunca "cero" sin una decisión
    /// explícita de síncrona) y (b) acotado por el intervalo de log shipping configurado (prueba que
    /// el RPO es predecible y gobernable por ese intervalo, no arbitrariamente grande).
    /// </summary>
    [Fact]
    public async Task LogShipping_UnderSimulatedLoad_ProducesBoundedNonZeroRpo()
    {
        var logShippingInterval = TimeSpan.FromSeconds(1.5);
        var loadDuration = TimeSpan.FromSeconds(6);

        var primaryConnectionString = BuildConnectionString(_primary, DatabaseName);
        var replicaConnectionString = BuildConnectionString(_replica, DatabaseName);
        var primaryMasterConnectionString = BuildConnectionString(_primary, "master");
        var replicaMasterConnectionString = BuildConnectionString(_replica, "master");

        // 1) Topología inicial: base primaria en recovery FULL (requisito para poder tomar backups
        //    de log — con SIMPLE no hay log shipping posible) + backup completo inicial restaurado en
        //    la réplica en estado NORECOVERY (a la espera de backups de log sucesivos), exactamente
        //    el procedimiento real de configuración de log shipping de SQL Server.
        await using (var primaryMaster = new SqlConnection(primaryMasterConnectionString))
        {
            await primaryMaster.OpenAsync();
            await ExecuteNonQueryAsync(primaryMaster, $"CREATE DATABASE [{DatabaseName}]");
            await ExecuteNonQueryAsync(primaryMaster, $"ALTER DATABASE [{DatabaseName}] SET RECOVERY FULL");
        }

        await using (var primaryDb = new SqlConnection(primaryConnectionString))
        {
            await primaryDb.OpenAsync();
            await ExecuteNonQueryAsync(
                primaryDb,
                "CREATE TABLE dbo.Ledger (" +
                "Id INT IDENTITY(1,1) PRIMARY KEY, " +
                "InsertedAtUtc DATETIME2(3) NOT NULL, " +
                "Payload NVARCHAR(50) NOT NULL)");
        }

        var fullBackupPath = $"{BackupDirectory}/full.bak";
        await using (var primaryMaster = new SqlConnection(primaryMasterConnectionString))
        {
            await primaryMaster.OpenAsync();
            await ExecuteNonQueryAsync(
                primaryMaster,
                $"BACKUP DATABASE [{DatabaseName}] TO DISK = N'{fullBackupPath}' WITH INIT");
        }

        await CopyFileBetweenContainersAsync(_primary, _replica, fullBackupPath);

        await using (var replicaMaster = new SqlConnection(replicaMasterConnectionString))
        {
            await replicaMaster.OpenAsync();
            await ExecuteNonQueryAsync(
                replicaMaster,
                $"RESTORE DATABASE [{DatabaseName}] FROM DISK = N'{fullBackupPath}' WITH NORECOVERY, REPLACE");
        }

        // 2) Carga simulada: inserts continuos en la primaria (transacciones confirmadas, como
        //    cualquier escritura real de negocio) mientras, en paralelo, un job de log shipping toma
        //    y aplica backups de log cada `logShippingInterval` — el mismo patrón productivo
        //    (backup de log periódico + restore en la secundaria), solo que a una escala de segundos
        //    en lugar de minutos para que la prueba sea ejecutable en un entorno de CI/desarrollo.
        using var loadCts = new CancellationTokenSource(loadDuration);
        var lastInsertedAtUtc = DateTime.MinValue;
        var insertLock = new object();

        var loadTask = Task.Run(async () =>
        {
            await using var primaryDb = new SqlConnection(primaryConnectionString);
            await primaryDb.OpenAsync();
            var sequence = 0;

            while (!loadCts.IsCancellationRequested)
            {
                sequence++;
                var insertedAtUtc = DateTime.UtcNow;

                await using (var command = primaryDb.CreateCommand())
                {
                    command.CommandText =
                        "INSERT INTO dbo.Ledger (InsertedAtUtc, Payload) VALUES (@insertedAtUtc, @payload)";
                    command.Parameters.AddWithValue("@insertedAtUtc", insertedAtUtc);
                    command.Parameters.AddWithValue("@payload", $"tx-{sequence}");
                    await command.ExecuteNonQueryAsync();
                }

                lock (insertLock)
                {
                    if (insertedAtUtc > lastInsertedAtUtc)
                    {
                        lastInsertedAtUtc = insertedAtUtc;
                    }
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), loadCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // fin del período de carga simulada.
                }
            }
        });

        var lastAppliedLogRestoreCompletedAtUtc = DateTime.MinValue;
        var lastReplicatedMaxInsertedAtUtc = DateTime.MinValue;
        var logSequence = 0;

        var logShippingTask = Task.Run(async () =>
        {
            await using var replicaMaster = new SqlConnection(replicaMasterConnectionString);
            await replicaMaster.OpenAsync();

            while (true)
            {
                try
                {
                    // Se observa el MISMO token que detiene la carga: si la "caída" ocurre mientras
                    // este ciclo todavía está esperando su turno, el ciclo se aborta sin tomar ni
                    // aplicar el backup de log pendiente — igual que en una caída real, donde el
                    // backup de log de la ventana en curso nunca llega a existir porque la primaria
                    // deja de estar disponible antes de completarlo. Esa ventana sin completar (desde
                    // el último ciclo que SÍ terminó hasta la caída) es, precisamente, el RPO.
                    await Task.Delay(logShippingInterval, loadCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                logSequence++;
                var logBackupPath = $"{BackupDirectory}/log_{logSequence}.trn";

                await using (var primaryMaster = new SqlConnection(primaryMasterConnectionString))
                {
                    await primaryMaster.OpenAsync();
                    await ExecuteNonQueryAsync(
                        primaryMaster,
                        $"BACKUP LOG [{DatabaseName}] TO DISK = N'{logBackupPath}' WITH INIT");
                }

                await CopyFileBetweenContainersAsync(_primary, _replica, logBackupPath);

                // WITH STANDBY deja la réplica en modo solo-lectura consultable entre restores de
                // log sucesivos (el mismo modo que usa el "log shipping" clásico de SQL Server para
                // permitir reportes sobre la secundaria) — así se puede medir, en cada ciclo, qué
                // datos ya llegaron a la réplica sin perder la cadena de restore de log.
                var standbyPath = $"{BackupDirectory}/undo_{logSequence}.bak";
                await ExecuteNonQueryAsync(
                    replicaMaster,
                    $"RESTORE LOG [{DatabaseName}] FROM DISK = N'{logBackupPath}' " +
                    $"WITH STANDBY = N'{standbyPath}'");

                var restoreCompletedAtUtc = DateTime.UtcNow;

                await using var replicaDb = new SqlConnection(replicaConnectionString);
                await replicaDb.OpenAsync();
                await using var command = replicaDb.CreateCommand();
                command.CommandText = "SELECT MAX(InsertedAtUtc) FROM dbo.Ledger";
                var result = await command.ExecuteScalarAsync();

                if (result is DateTime maxInsertedAtUtc)
                {
                    lastAppliedLogRestoreCompletedAtUtc = restoreCompletedAtUtc;
                    lastReplicatedMaxInsertedAtUtc = maxInsertedAtUtc;
                }
            }
        });

        await loadTask;
        // Momento de la "caída" simulada: el instante en que la primaria deja de estar disponible,
        // inmediatamente después del último insert confirmado — el tail del log de transacciones de
        // ese instante en adelante se considera perdido (no se toma un backup de log final "de cola",
        // que es exactamente lo que asume un escenario de caída real sin tail-log backup posible).
        var disasterAtUtc = lastInsertedAtUtc;

        // `loadCts` ya se canceló (por el timeout de `loadDuration`, compartido por ambas tareas):
        // el ciclo de log shipping que estuviera esperando su turno en ese instante se aborta sin
        // completarse — no se aplica ningún backup de log posterior al momento de la caída.
        await logShippingTask;

        lastReplicatedMaxInsertedAtUtc.Should().NotBe(
            DateTime.MinValue,
            "al menos un ciclo de log shipping debe haberse aplicado durante la carga simulada");

        var rpo = disasterAtUtc - lastReplicatedMaxInsertedAtUtc;

        var evidence = new StringBuilder()
            .AppendLine($"Último insert confirmado en la primaria (momento de la caída simulada): {disasterAtUtc:O}")
            .AppendLine($"Último dato confirmado en la réplica (último restore de log aplicado):  {lastReplicatedMaxInsertedAtUtc:O}")
            .AppendLine($"Ciclos de log shipping ejecutados: {logSequence}")
            .AppendLine($"Intervalo de log shipping configurado: {logShippingInterval}")
            .AppendLine($"RPO medido (dato confirmado y perdido = disaster - último dato replicado): {rpo}")
            .ToString();
        _output.WriteLine(evidence);

        // (a) RPO > 0: la réplica, en el momento de la caída, no tiene el último dato confirmado en
        // la primaria — prueba que el mecanismo es asíncrono (RPO no-cero), consistente con los
        // perfiles Standard/Gold del BIA (docs/bia-fase5.md), nunca "RPO cero" sin decisión explícita
        // de replicación síncrona (reservada a Platinum, ver docs/replicacion-sql-fase5.md sección 3).
        rpo.Should().BeGreaterThan(
            TimeSpan.Zero,
            "el log shipping es asíncrono: siempre debe existir una ventana de datos confirmados en " +
            "la primaria que todavía no llegaron a la réplica en el momento de la caída" + Environment.NewLine + evidence);

        // (b) RPO acotado por el intervalo de log shipping configurado: el atraso no puede ser
        // arbitrariamente grande, tiene que estar gobernado por la frecuencia de los backups de log
        // (más un margen para el tiempo de copia/restore/red, aquí generoso porque los tres
        // contenedores comparten el mismo host de desarrollo). Esto es lo que hace al RPO
        // "predecible y gobernable", no solo "no-cero".
        rpo.Should().BeLessThan(
            logShippingInterval * 3,
            "el RPO debe estar acotado por el intervalo de log shipping configurado (mecanismo " +
            "predecible), no ser arbitrariamente grande" + Environment.NewLine + evidence);
    }

    private static async Task CopyFileBetweenContainersAsync(
        MsSqlContainer source,
        MsSqlContainer destination,
        string containerPath)
    {
        var fileContent = await source.ReadFileAsync(containerPath);
        await destination.CopyAsync(fileContent, containerPath);
    }

    private static async Task ExecuteNonQueryAsync(SqlConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static string BuildConnectionString(MsSqlContainer container, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = databaseName,
            // Sin pooling: una conexión "pooled" cerrada por el cliente sigue físicamente conectada
            // a esa base de datos en el servidor mientras vive en el pool, y RESTORE (tanto completo
            // como de log) exige acceso exclusivo — sin esto, el segundo ciclo de log shipping falla
            // con "Exclusive access could not be obtained because the database is in use" apenas la
            // conexión usada para leer MAX(InsertedAtUtc) en el ciclo anterior queda pooled.
            Pooling = false,
        };

        return builder.ConnectionString;
    }
}
