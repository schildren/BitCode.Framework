using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit.Abstractions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F5-08 (Fase 5 — Disaster Recovery y multi-región): valida, contra dos instancias reales de SQL
/// Server (mismo patrón "origen"/"destino" de <see cref="SqlBackupRestoreIntegrationTests"/> de
/// F5-07), que un point-in-time restore (<c>RESTORE LOG ... WITH STOPAT</c>) sobre la misma cadena
/// <c>full → differential → log</c> puede detenerse en un instante exacto UBICADO ENTRE DOS
/// TRANSACCIONES DE NEGOCIO CONOCIDAS, no solo al final del último backup de log disponible: los
/// datos confirmados antes del <c>STOPAT</c> elegido sobreviven, los confirmados después NO.
/// </summary>
/// <remarks>
/// Igual que <see cref="SqlBackupRestoreIntegrationTests"/>, esta prueba NO reimplementa la lógica
/// T-SQL de los backups: lee el contenido real de <c>tools/SqlBackupAutomation/Full-Backup.sql</c>,
/// <c>Differential-Backup.sql</c> y <c>Log-Backup.sql</c> del repositorio y sustituye sus variables
/// <c>$(Variable)</c> exactamente como lo haría <c>sqlcmd -v</c> — el <c>RESTORE ... WITH STOPAT</c>
/// en sí no tiene script propio en <c>tools/SqlBackupAutomation/</c> (es un comando de restauración
/// ejecutado por un operador durante un incidente, no un job programado; ver
/// <c>docs/runbook-pitr-fase5.md</c> para el procedimiento completo), así que esta prueba lo emite
/// directamente contra el servidor, con el mismo texto que documenta el runbook.
///
/// El <c>STOPAT</c> se calcula con la hora del PROPIO SERVIDOR de origen (<c>SYSDATETIME()</c>),
/// nunca con el reloj del host que ejecuta la prueba — evita cualquier desfase de reloj entre el
/// proceso de test y el contenedor, el mismo motivo por el que
/// <c>SqlLogShippingRpoIntegrationTests</c> (F5-04) mide el lag consultando al propio servidor.
/// </remarks>
public sealed class SqlPointInTimeRestoreIntegrationTests : IAsyncLifetime
{
    private const string DatabaseName = "PitrDemo";
    private const string BackupDirectory = "/tmp/sqlbackups-pitr";

    private readonly MsSqlContainer _source = new MsSqlBuilder().Build();
    private readonly MsSqlContainer _destination = new MsSqlBuilder().Build();
    private readonly ITestOutputHelper _output;

    public SqlPointInTimeRestoreIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_source.StartAsync(), _destination.StartAsync());

        await _source.ExecAsync(["mkdir", "-p", BackupDirectory]);
        await _source.ExecAsync(["chmod", "777", BackupDirectory]);
        await _destination.ExecAsync(["mkdir", "-p", BackupDirectory]);
        await _destination.ExecAsync(["chmod", "777", BackupDirectory]);
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(_source.DisposeAsync().AsTask(), _destination.DisposeAsync().AsTask());
    }

    /// <summary>
    /// Criterio de aceptación de F5-08 ("Tiempo medido"): un PITR real, aplicado a un
    /// <c>STOPAT</c> ubicado ENTRE dos transacciones de negocio conocidas y separadas en el
    /// tiempo, reconstruye exactamente los datos confirmados antes del punto elegido y excluye
    /// los confirmados después — incluidos los de un backup de log posterior que ni siquiera se
    /// llega a aplicar (el operador elige, según el runbook, hasta qué log aplicar). Se mide el
    /// tiempo real de la secuencia completa de restore.
    /// </summary>
    [Fact]
    public async Task PointInTimeRestore_StopsExactlyBetweenTwoKnownTransactions()
    {
        var sourceMasterConnectionString = BuildConnectionString(_source, "master");
        var sourceDbConnectionString = BuildConnectionString(_source, DatabaseName);
        var destinationMasterConnectionString = BuildConnectionString(_destination, "master");
        var destinationDbConnectionString = BuildConnectionString(_destination, DatabaseName);

        // 1) Base en RECOVERY FULL (mismo requisito que F5-07) + tabla de negocio de ejemplo.
        await using (var sourceMaster = new SqlConnection(sourceMasterConnectionString))
        {
            await sourceMaster.OpenAsync();
            await ExecuteNonQueryAsync(sourceMaster, $"CREATE DATABASE [{DatabaseName}]");
            await ExecuteNonQueryAsync(sourceMaster, $"ALTER DATABASE [{DatabaseName}] SET RECOVERY FULL");
        }

        await using (var sourceDb = new SqlConnection(sourceDbConnectionString))
        {
            await sourceDb.OpenAsync();
            await ExecuteNonQueryAsync(
                sourceDb,
                "CREATE TABLE dbo.Pedidos (" +
                "Id INT IDENTITY(1,1) PRIMARY KEY, " +
                "Payload NVARCHAR(50) NOT NULL)");
        }

        // 2) Lote previo al FULL.
        await InsertBatchAsync(sourceDbConnectionString, "antes-full", 1, 2);

        var fullBackupPath = $"{BackupDirectory}/full.bak";
        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Full-Backup.sql",
            new Dictionary<string, string> { ["DatabaseName"] = DatabaseName, ["BackupPath"] = fullBackupPath });

        // 3) Lote previo al DIFFERENTIAL.
        await InsertBatchAsync(sourceDbConnectionString, "antes-diff", 3, 4);

        var differentialBackupPath = $"{BackupDirectory}/differential.bak";
        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Differential-Backup.sql",
            new Dictionary<string, string> { ["DatabaseName"] = DatabaseName, ["BackupPath"] = differentialBackupPath });

        // 4) TRANSACCIÓN DE NEGOCIO CONOCIDA #1 ("pedido-1"): debe SOBREVIVIR al PITR.
        await InsertBatchAsync(sourceDbConnectionString, "pedido-1", 5, 6);
        var timestampAfterTransaction1 = await ReadServerTimestampAsync(sourceDbConnectionString);

        // Separación real en el tiempo entre las dos transacciones — evita cualquier ambigüedad
        // de redondeo de datetime2 al elegir el STOPAT entre ambas (ver docs/runbook-pitr-fase5.md
        // sección 4 sobre la resolución del timestamp elegido).
        await Task.Delay(TimeSpan.FromSeconds(2));

        var logBackup1Path = $"{BackupDirectory}/log1.trn";
        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Log-Backup.sql",
            new Dictionary<string, string> { ["DatabaseName"] = DatabaseName, ["BackupPath"] = logBackup1Path });

        // 5) TRANSACCIÓN DE NEGOCIO CONOCIDA #2 ("pedido-2"): debe QUEDAR EXCLUIDA del PITR — es
        // el "borrado/corrupción lógica posterior al punto de recuperación deseado" que un PITR
        // real busca evitar reproducir.
        await InsertBatchAsync(sourceDbConnectionString, "pedido-2", 7, 8);
        var timestampAfterTransaction2 = await ReadServerTimestampAsync(sourceDbConnectionString);

        await Task.Delay(TimeSpan.FromSeconds(2));

        var logBackup2Path = $"{BackupDirectory}/log2.trn";
        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Log-Backup.sql",
            new Dictionary<string, string> { ["DatabaseName"] = DatabaseName, ["BackupPath"] = logBackup2Path });

        // 6) TRANSACCIÓN DE NEGOCIO POSTERIOR ("pedido-3") + tercer backup de log: existe, pero el
        // runbook NUNCA debe necesitar aplicarlo para llegar al STOPAT elegido (queda más adelante
        // en el tiempo) — se demuestra explícitamente que ese log no se copia ni se restaura.
        await InsertBatchAsync(sourceDbConnectionString, "pedido-3", 9, 10);

        var logBackup3Path = $"{BackupDirectory}/log3.trn";
        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Log-Backup.sql",
            new Dictionary<string, string> { ["DatabaseName"] = DatabaseName, ["BackupPath"] = logBackup3Path });

        // El STOPAT elegido es el punto medio EXACTO entre el commit de "pedido-1" (debe
        // sobrevivir) y el de "pedido-2" (debe excluirse) — el escenario literal del criterio de
        // aceptación: "restaurando hasta un STOPAT específico ubicado entre dos transacciones
        // conocidas".
        var stopAt = timestampAfterTransaction1 +
            TimeSpan.FromTicks((timestampAfterTransaction2 - timestampAfterTransaction1).Ticks / 2);
        var stopAtLiteral = stopAt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        // 7) Copia real de SOLO los backups necesarios hasta el STOPAT (full + differential +
        // log1 + log2) — log3 NUNCA se copia al destino: es la evidencia de que el runbook elige
        // hasta qué log de transacciones aplicar, no "todos los disponibles" (ver
        // docs/runbook-pitr-fase5.md sección 3, paso de selección de logs).
        await CopyFileBetweenContainersAsync(_source, _destination, fullBackupPath);
        await CopyFileBetweenContainersAsync(_source, _destination, differentialBackupPath);
        await CopyFileBetweenContainersAsync(_source, _destination, logBackup1Path);
        await CopyFileBetweenContainersAsync(_source, _destination, logBackup2Path);

        // 8) Secuencia real de restore, cronometrada de punta a punta: full WITH NORECOVERY ->
        // differential WITH NORECOVERY -> log1 WITH NORECOVERY (íntegro, queda antes del STOPAT
        // por construcción) -> log2 WITH STOPAT '<punto medio>' WITH RECOVERY (dentro de este log
        // es donde ocurre el corte real).
        var stopwatch = Stopwatch.StartNew();

        await using (var destinationMaster = new SqlConnection(destinationMasterConnectionString))
        {
            await destinationMaster.OpenAsync();

            await ExecuteNonQueryAsync(
                destinationMaster,
                $"RESTORE DATABASE [{DatabaseName}] FROM DISK = N'{fullBackupPath}' WITH NORECOVERY, REPLACE");

            await ExecuteNonQueryAsync(
                destinationMaster,
                $"RESTORE DATABASE [{DatabaseName}] FROM DISK = N'{differentialBackupPath}' WITH NORECOVERY");

            await ExecuteNonQueryAsync(
                destinationMaster,
                $"RESTORE LOG [{DatabaseName}] FROM DISK = N'{logBackup1Path}' WITH NORECOVERY");

            await ExecuteNonQueryAsync(
                destinationMaster,
                $"RESTORE LOG [{DatabaseName}] FROM DISK = N'{logBackup2Path}' " +
                $"WITH STOPAT = N'{stopAtLiteral}', RECOVERY");
        }

        stopwatch.Stop();

        // 9) Verificación de negocio: "pedido-1" (y todo lo anterior) sobrevive; "pedido-2" y
        // "pedido-3" (posteriores al STOPAT) no aparecen — ni siquiera se llegó a copiar el log
        // que contiene a "pedido-3".
        var survivingLabels = await ReadDistinctLabelPrefixesAsync(destinationDbConnectionString);

        var evidence = new StringBuilder()
            .AppendLine($"STOPAT elegido (entre pedido-1 y pedido-2): {stopAtLiteral}")
            .AppendLine($"Etiquetas presentes tras el PITR: {string.Join(", ", survivingLabels)}")
            .AppendLine($"Tiempo real de la secuencia de restore (full+differential+log1+log2 WITH STOPAT): {stopwatch.Elapsed.TotalMilliseconds:F0} ms")
            .ToString();
        _output.WriteLine(evidence);

        survivingLabels.Should().Contain(
            ["antes-full", "antes-diff", "pedido-1"],
            "todo lo confirmado antes del STOPAT elegido (incluida la transacción conocida 'pedido-1') debe sobrevivir" + Environment.NewLine + evidence);
        survivingLabels.Should().NotContain(
            ["pedido-2", "pedido-3"],
            "todo lo confirmado después del STOPAT elegido (incluida la transacción conocida 'pedido-2', y 'pedido-3' cuyo log ni se copió) debe quedar excluido" + Environment.NewLine + evidence);

        stopwatch.Elapsed.Should().BePositive(
            "la secuencia de restore debe haberse ejecutado realmente (tiempo medible), no simulado" + Environment.NewLine + evidence);
    }

    private static async Task InsertBatchAsync(string connectionString, string label, int from, int to)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        for (var i = from; i <= to; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO dbo.Pedidos (Payload) VALUES (@payload)";
            command.Parameters.AddWithValue("@payload", $"{label}-{i}");
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Lee la hora ACTUAL DEL SERVIDOR de origen (nunca el reloj del host que corre la prueba) —
    /// el mismo criterio que exige un PITR real: el <c>STOPAT</c> se elige en términos del reloj
    /// del servidor cuyo log de transacciones se está restaurando.
    /// </summary>
    private static async Task<DateTime> ReadServerTimestampAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SYSDATETIME()";
        var result = await command.ExecuteScalarAsync();
        return (DateTime)result!;
    }

    private static async Task<HashSet<string>> ReadDistinctLabelPrefixesAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM dbo.Pedidos";
        await using var reader = await command.ExecuteReaderAsync();

        var prefixes = new HashSet<string>();
        while (await reader.ReadAsync())
        {
            var payload = reader.GetString(0);
            var lastDash = payload.LastIndexOf('-');
            prefixes.Add(lastDash > 0 ? payload[..lastDash] : payload);
        }

        return prefixes;
    }

    /// <summary>
    /// Lee el script T-SQL real de <c>tools/SqlBackupAutomation/</c>, sustituye sus variables
    /// <c>$(Nombre)</c> (misma sintaxis que <c>sqlcmd -v</c>) y ejecuta el resultado contra el
    /// contenedor indicado — mismo mecanismo que <c>SqlBackupRestoreIntegrationTests</c> (F5-07),
    /// sin reescribir la lógica del script en el test.
    /// </summary>
    private static async Task RunSqlScriptAsync(
        MsSqlContainer container,
        string connectionString,
        string scriptFileName,
        Dictionary<string, string> variables)
    {
        var scriptPath = Path.Combine(FindRepoRoot(), "tools", "SqlBackupAutomation", scriptFileName);
        var scriptText = await File.ReadAllTextAsync(scriptPath);

        foreach (var (name, value) in variables)
        {
            scriptText = scriptText.Replace($"$({name})", value);
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteNonQueryAsync(connection, scriptText);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BitCode.Framework.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("No se encontró la raíz del repo (BitCode.Framework.slnx).");
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
            // Sin pooling: RESTORE exige acceso exclusivo a la base — una conexión pooled cerrada
            // por el cliente sigue físicamente conectada mientras vive en el pool (mismo motivo
            // documentado en SqlBackupRestoreIntegrationTests de F5-07 y SqlLogShippingRpoIntegrationTests de F5-04).
            Pooling = false,
        };

        return builder.ConnectionString;
    }
}
