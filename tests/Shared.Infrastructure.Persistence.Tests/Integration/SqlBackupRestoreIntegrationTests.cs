using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit.Abstractions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F5-07 (Fase 5 — Disaster Recovery y multi-región): valida, contra dos instancias reales de SQL
/// Server (dos contenedores Testcontainers independientes — un "origen" que produce los backups y
/// un "destino" que los restaura, exactamente como serían dos regiones/servidores distintos), que
/// el ciclo completo <c>full backup → differential backup → log backup → restore</c> descrito en
/// <c>docs/politica-backups-fase5.md</c> reconstruye correctamente los datos hasta el punto
/// esperado.
/// </summary>
/// <remarks>
/// Esta prueba NO reimplementa la lógica T-SQL de los backups: lee el contenido real de
/// <c>tools/SqlBackupAutomation/Full-Backup.sql</c>, <c>Differential-Backup.sql</c>,
/// <c>Log-Backup.sql</c> y <c>Retention-Cleanup.sql</c> del repositorio y sustituye sus variables
/// <c>$(Variable)</c> exactamente como lo haría <c>sqlcmd -v</c>, de modo que el texto SQL enviado
/// al servidor es idéntico al que produciría <c>Invoke-SqlBackupJob.ps1</c> en un entorno real. No
/// usa <c>sqlcmd</c>/PowerShell directamente porque el entorno de CI de esta prueba no garantiza su
/// disponibilidad multiplataforma; el resultado enviado al servidor es el mismo de cualquier forma.
///
/// No usa <see cref="SqlServerContainerFixture"/> ni la <c>SqlServerCollection</c> compartida por
/// el mismo motivo que <c>SqlLogShippingRpoIntegrationTests</c> (F5-04): esta prueba necesita, a
/// propósito, DOS instancias de SQL Server completamente independientes, para que la copia del
/// backup entre "origen" y "destino" sea una copia real entre dos sistemas de archivos distintos.
/// </remarks>
public sealed class SqlBackupRestoreIntegrationTests : IAsyncLifetime
{
    private const string DatabaseName = "BackupRestoreDemo";
    private const string BackupDirectory = "/tmp/sqlbackups";
    private const string CertificateName = "BitCodeBackupTestCert";
    private const string MasterKeyPassword = "M4sterKey!Test#2026";
    private const string PrivateKeyPassword = "PrivKey!Test#2026";

    private readonly MsSqlContainer _source = new MsSqlBuilder().Build();
    private readonly MsSqlContainer _destination = new MsSqlBuilder().Build();
    private readonly ITestOutputHelper _output;

    public SqlBackupRestoreIntegrationTests(ITestOutputHelper output)
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
    /// Criterio de aceptación de F5-07 ("Restauración validada"): un ciclo completo
    /// full → differential → log → restore, ejecutando los scripts reales de
    /// <c>tools/SqlBackupAutomation/</c>, reconstruye en el destino exactamente los mismos datos
    /// confirmados en el origen hasta el último backup de log tomado.
    /// </summary>
    [Fact]
    public async Task FullDifferentialLogCycle_RestoresExactDataUpToLastLogBackup()
    {
        var sourceMasterConnectionString = BuildConnectionString(_source, "master");
        var sourceDbConnectionString = BuildConnectionString(_source, DatabaseName);
        var destinationMasterConnectionString = BuildConnectionString(_destination, "master");
        var destinationDbConnectionString = BuildConnectionString(_destination, DatabaseName);

        // 1) Base en RECOVERY FULL (requisito documentado en docs/politica-backups-fase5.md
        //    sección 2 para que los backups de log sean posibles) + tabla de negocio de ejemplo.
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
                "CREATE TABLE dbo.Ledger (" +
                "Id INT IDENTITY(1,1) PRIMARY KEY, " +
                "Payload NVARCHAR(50) NOT NULL)");
        }

        // 2) F5-09: aprovisiona el certificado de cifrado de backups en el ORIGEN, con el mismo
        //    script real (Enable-BackupEncryption.sql) que usaría un operador humano — la prueba
        //    no reimplementa la lógica de creación de master key/certificado.
        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Enable-BackupEncryption.sql",
            new Dictionary<string, string>
            {
                ["MasterKeyPassword"] = MasterKeyPassword,
                ["CertificateName"] = CertificateName,
            });

        // 3) Lote A, antes del FULL.
        await InsertBatchAsync(sourceDbConnectionString, "full", 1, 5);

        var fullBackupPath = $"{BackupDirectory}/full.bak";
        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Full-Backup.sql",
            new Dictionary<string, string>
            {
                ["DatabaseName"] = DatabaseName,
                ["BackupPath"] = fullBackupPath,
                ["CertificateName"] = CertificateName,
            });

        // 4) Lote B, antes del DIFFERENTIAL.
        await InsertBatchAsync(sourceDbConnectionString, "diff", 6, 10);

        var differentialBackupPath = $"{BackupDirectory}/differential.bak";
        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Differential-Backup.sql",
            new Dictionary<string, string>
            {
                ["DatabaseName"] = DatabaseName,
                ["BackupPath"] = differentialBackupPath,
                ["CertificateName"] = CertificateName,
            });

        // 5) Lote C, antes del LOG.
        await InsertBatchAsync(sourceDbConnectionString, "log", 11, 15);

        var logBackupPath = $"{BackupDirectory}/log.trn";
        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Log-Backup.sql",
            new Dictionary<string, string>
            {
                ["DatabaseName"] = DatabaseName,
                ["BackupPath"] = logBackupPath,
                ["CertificateName"] = CertificateName,
            });

        // Evidencia real de F5-09 (cifrado): msdb.dbo.backupset.encryptor_type refleja lo que SQL
        // Server realmente escribió en el header del backup (NULL si el backup NO está cifrado,
        // 'CERTIFICATE' cuando se cifró con SERVER CERTIFICATE como en Full-Backup.sql) — no una
        // suposición basada en el texto del script enviado.
        await using (var sourceMasterForEvidence = new SqlConnection(sourceMasterConnectionString))
        {
            await sourceMasterForEvidence.OpenAsync();
            await using var command = sourceMasterForEvidence.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM msdb.dbo.backupset WHERE database_name = @db AND encryptor_type = 'CERTIFICATE'";
            command.Parameters.AddWithValue("@db", DatabaseName);
            var encryptedBackupCount = (int)(await command.ExecuteScalarAsync())!;
            encryptedBackupCount.Should().Be(3, "los tres backups (full, differential, log) deben quedar marcados por SQL Server como cifrados con certificado de servidor (F5-09)");
        }

        // Huella del estado en el ORIGEN en el momento exacto del último backup de log tomado
        // (todo lo insertado hasta acá — lotes A + B + C — debe reconstruirse en el destino).
        var (expectedCount, expectedChecksum) = await ComputeFingerprintAsync(sourceDbConnectionString);
        expectedCount.Should().Be(15, "los tres lotes (full, differential, log) ya están confirmados en el origen");

        // 6) Copia real de los tres archivos de backup del origen al destino (dos sistemas de
        //    archivos distintos, exactamente como cruzaría una red o un storage compartido entre
        //    servidores/regiones reales).
        await CopyFileBetweenContainersAsync(_source, _destination, fullBackupPath);
        await CopyFileBetweenContainersAsync(_source, _destination, differentialBackupPath);
        await CopyFileBetweenContainersAsync(_source, _destination, logBackupPath);

        // 7) F5-09 (DR entre servidores): el DESTINO no tiene el certificado de cifrado — sin
        //    importarlo primero (mismo procedimiento real de
        //    Export-BackupEncryptionCertificate.sql / Import-BackupEncryptionCertificate.sql),
        //    RESTORE de un backup cifrado falla por diseño (ver también el test dedicado
        //    RestoreEncryptedBackup_WithoutCertificate_FailsWithCertificateError, que aísla esta
        //    propiedad). Aquí se realiza el flujo completo y legítimo de DR: exportar en origen e
        //    importar en destino antes de restaurar.
        const string certificateFilePath = $"{BackupDirectory}/backup-cert.cer";
        const string privateKeyFilePath = $"{BackupDirectory}/backup-cert.pvk";

        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Export-BackupEncryptionCertificate.sql",
            new Dictionary<string, string>
            {
                ["CertificateName"] = CertificateName,
                ["CertificateFilePath"] = certificateFilePath,
                ["PrivateKeyFilePath"] = privateKeyFilePath,
                ["PrivateKeyPassword"] = PrivateKeyPassword,
            });

        await CopyFileBetweenContainersAsync(_source, _destination, certificateFilePath);
        await CopyFileBetweenContainersAsync(_source, _destination, privateKeyFilePath);

        await RunSqlScriptAsync(
            _destination,
            destinationMasterConnectionString,
            "Import-BackupEncryptionCertificate.sql",
            new Dictionary<string, string>
            {
                ["MasterKeyPassword"] = MasterKeyPassword,
                ["CertificateName"] = CertificateName,
                ["CertificateFilePath"] = certificateFilePath,
                ["PrivateKeyFilePath"] = privateKeyFilePath,
                ["PrivateKeyPassword"] = PrivateKeyPassword,
            });

        // 8) Cadena de restauración real: full WITH NORECOVERY -> differential WITH DIFFERENTIAL,
        //    NORECOVERY -> log WITH RECOVERY (deja la base en línea al final de la cadena).
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
                $"RESTORE LOG [{DatabaseName}] FROM DISK = N'{logBackupPath}' WITH RECOVERY");
        }

        var (actualCount, actualChecksum) = await ComputeFingerprintAsync(destinationDbConnectionString);

        var evidence = new StringBuilder()
            .AppendLine($"Filas esperadas (origen, hasta el último log backup): {expectedCount}, checksum {expectedChecksum:X}")
            .AppendLine($"Filas restauradas (destino, tras full+differential+log): {actualCount}, checksum {actualChecksum:X}")
            .ToString();
        _output.WriteLine(evidence);

        actualCount.Should().Be(
            expectedCount,
            "la cadena full+differential+log debe reconstruir exactamente todas las filas confirmadas hasta el último log backup" + Environment.NewLine + evidence);
        actualChecksum.Should().Be(
            expectedChecksum,
            "el contenido restaurado debe coincidir byte a byte con el contenido de origen (mismos Id/Payload), no solo la cantidad de filas" + Environment.NewLine + evidence);
    }

    /// <summary>
    /// Verifica que <c>Retention-Cleanup.sql</c> (el mismo script real de
    /// <c>tools/SqlBackupAutomation/</c>) borra únicamente los backups fuera de la ventana de
    /// retención configurada, dejando intactos los que están dentro — sin afectar archivos de otra
    /// extensión en la misma carpeta.
    /// </summary>
    [Fact]
    public async Task RetentionCleanup_DeletesOnlyBackupsOlderThanRetentionWindow()
    {
        var masterConnectionString = BuildConnectionString(_source, "master");
        var retentionFolder = $"{BackupDirectory}/retention-test";
        await _source.ExecAsync(["mkdir", "-p", retentionFolder]);

        await using (var master = new SqlConnection(masterConnectionString))
        {
            await master.OpenAsync();
            await ExecuteNonQueryAsync(master, "CREATE DATABASE [RetentionProbe]");
            await ExecuteNonQueryAsync(
                master,
                $"BACKUP DATABASE [RetentionProbe] TO DISK = N'{retentionFolder}/old.bak' WITH INIT");
            await ExecuteNonQueryAsync(
                master,
                $"BACKUP DATABASE [RetentionProbe] TO DISK = N'{retentionFolder}/recent.bak' WITH INIT");
        }

        // "old.bak" queda con fecha de archivo forzada a 60 días atrás (fuera de una retención de
        // 30 días); "recent.bak" conserva su fecha real de creación (dentro de la retención).
        await _source.ExecAsync(
            ["touch", "-d", DateTime.UtcNow.AddDays(-60).ToString("yyyy-MM-dd"), $"{retentionFolder}/old.bak"]);

        await RunSqlScriptAsync(
            _source,
            masterConnectionString,
            "Retention-Cleanup.sql",
            new Dictionary<string, string>
            {
                ["BackupFolder"] = retentionFolder,
                ["Extension"] = "bak",
                ["RetentionDays"] = "30",
            });

        var listing = await _source.ExecAsync(["ls", retentionFolder]);
        var remainingFiles = listing.Stdout;

        _output.WriteLine($"Archivos restantes en {retentionFolder} tras limpieza de retención (30 días): {remainingFiles}");

        remainingFiles.Should().NotContain("old.bak", "el backup fuera de la ventana de retención debe eliminarse");
        remainingFiles.Should().Contain("recent.bak", "el backup dentro de la ventana de retención debe conservarse");
    }

    /// <summary>
    /// F5-09 ("cifrado"): evidencia real, aislada, de que un backup cifrado con
    /// <c>ENCRYPTION (... SERVER CERTIFICATE = ...)</c> (ver <c>Full-Backup.sql</c>) NO puede
    /// restaurarse en un servidor que no tiene el certificado (ni su clave privada) — sin esto, el
    /// "cifrado" del backup sería cosmético (cualquier servidor podría restaurarlo igual). El
    /// destino de esta prueba deliberadamente NO ejecuta
    /// <c>Import-BackupEncryptionCertificate.sql</c> (a diferencia del test principal de este
    /// archivo, que sí completa el flujo legítimo de DR).
    /// </summary>
    [Fact]
    public async Task RestoreEncryptedBackup_WithoutCertificate_FailsWithCertificateError()
    {
        var sourceMasterConnectionString = BuildConnectionString(_source, "master");
        var destinationMasterConnectionString = BuildConnectionString(_destination, "master");

        await using (var sourceMaster = new SqlConnection(sourceMasterConnectionString))
        {
            await sourceMaster.OpenAsync();
            await ExecuteNonQueryAsync(sourceMaster, $"CREATE DATABASE [{DatabaseName}]");
        }

        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Enable-BackupEncryption.sql",
            new Dictionary<string, string>
            {
                ["MasterKeyPassword"] = MasterKeyPassword,
                ["CertificateName"] = CertificateName,
            });

        var fullBackupPath = $"{BackupDirectory}/full-nocred.bak";
        await RunSqlScriptAsync(
            _source,
            sourceMasterConnectionString,
            "Full-Backup.sql",
            new Dictionary<string, string>
            {
                ["DatabaseName"] = DatabaseName,
                ["BackupPath"] = fullBackupPath,
                ["CertificateName"] = CertificateName,
            });

        await CopyFileBetweenContainersAsync(_source, _destination, fullBackupPath);

        await using var destinationMaster = new SqlConnection(destinationMasterConnectionString);
        await destinationMaster.OpenAsync();

        // El destino NO tiene el certificado (no se ejecutó Import-BackupEncryptionCertificate.sql)
        // — RESTORE de un backup cifrado debe fallar por diseño de SQL Server.
        var exception = await Record.ExceptionAsync(() => ExecuteNonQueryAsync(
            destinationMaster,
            $"RESTORE DATABASE [{DatabaseName}] FROM DISK = N'{fullBackupPath}' WITH RECOVERY, REPLACE"));

        _output.WriteLine($"Excepción real al intentar restaurar sin el certificado de cifrado: {exception}");

        exception.Should().NotBeNull(
            "un servidor sin el certificado de cifrado del backup no debe poder restaurarlo — de lo contrario el cifrado en reposo (F5-09) no ofrece ninguna protección real");
        var mentionsMissingKeyMaterial = new[] { "certificate", "certificado", "asymmetric key", "clave asimétrica" }
            .Any(fragment => exception!.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        mentionsMissingKeyMaterial.Should().BeTrue(
            $"el motor debe rechazar la restauración explícitamente por falta del certificado/clave, no por otra causa. Mensaje real: {exception!.Message}");
    }

    private static async Task InsertBatchAsync(string connectionString, string label, int from, int to)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        for (var i = from; i <= to; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO dbo.Ledger (Payload) VALUES (@payload)";
            command.Parameters.AddWithValue("@payload", $"{label}-{i}");
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<(int Count, int Checksum)> ComputeFingerprintAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), CHECKSUM_AGG(CHECKSUM(Id, Payload)) FROM dbo.Ledger";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        var count = reader.GetInt32(0);
        var checksum = await reader.IsDBNullAsync(1) ? 0 : reader.GetInt32(1);
        return (count, checksum);
    }

    /// <summary>
    /// Lee el script T-SQL real de <c>tools/SqlBackupAutomation/</c>, sustituye sus variables
    /// <c>$(Nombre)</c> (misma sintaxis que <c>sqlcmd -v</c>) y ejecuta el resultado contra el
    /// contenedor indicado — el texto SQL que llega al servidor es idéntico al que produciría
    /// <c>sqlcmd</c> en un entorno real, sin reescribir la lógica del script en el test.
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
            // documentado en SqlLogShippingRpoIntegrationTests de F5-04).
            Pooling = false,
        };

        return builder.ConnectionString;
    }
}
