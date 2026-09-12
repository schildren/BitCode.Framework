using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BitCode.Framework.Tools.Migrations.Core.DataReconciliation;

/// <summary>
/// Herramienta de reconciliación de datos (Fase 9, F9-08 "Data migration" —
/// <c>docs/plan-maestro-bitcode-ia.md</c>, backlog de Fase 9, entregable "Herramienta", criterio de
/// aceptación "Conteos y hashes conciliados"). Compara, tabla por tabla, un origen y un destino que
/// comparten el mismo modelo de EF Core: conteo de filas y un hash de contenido determinístico que
/// detecta discrepancias de datos aunque los conteos coincidan.
/// </summary>
/// <remarks>
/// <para>
/// <b>Escenario objetivo:</b> este framework separó el store de cada módulo de plataforma desde Fase 6
/// (ver F9-03, "Data ownership" — <c>WorkflowDbContext</c> nunca compartió base de datos con otro módulo,
/// confirmado con evidencia real en <c>WorkflowDataOwnershipIntegrationTests</c>). Por lo tanto, F9-08 no
/// consiste en "separar filas mezcladas en una base compartida" (ese trabajo no existe en este framework),
/// sino en la herramienta que SÍ sería necesaria en una extracción real de microservicio: mover el store
/// físico de un módulo (p. ej. Workflow) de un servidor SQL Server a otro sin downtime, o consolidar y
/// particionar datos, y demostrar con evidencia real que el destino contiene exactamente lo mismo que el
/// origen.
/// </para>
/// <para>
/// <b>Diseño específico de módulo, generalizable después:</b> esta clase no está acoplada a
/// <c>WorkflowDbContext</c> — descubre las tablas a reconciliar a partir del <see cref="IModel"/> de
/// cualquier <c>DbContext</c> de EF Core (mismo mecanismo de reflexión sobre ensamblado/DbContext que ya
/// usa <c>BitCode.Migrations</c> para <c>validate</c>/<c>status</c>/<c>migrate</c>/<c>rollback</c>). Se
/// demuestra y prueba contra <c>WorkflowDbContext</c> porque es el módulo piloto de extracción de
/// microservicios (ADR-0020), pero el mismo comando <c>reconcile</c> sirve, sin cambios de código, para
/// cualquier otro <c>DbContext</c> del framework. Lo que queda como trabajo futuro (no alcanzado por esta
/// tarea) es soportar escenarios de modelo verdaderamente heterogéneo entre origen y destino (por ejemplo,
/// reconciliar contra un esquema ya transformado/particionado con nombres de columna distintos) — hoy se
/// asume que origen y destino comparten exactamente el mismo <see cref="IModel"/>.
/// </para>
/// <para>
/// <b>Limitaciones honestas:</b>
/// </para>
/// <list type="bullet">
/// <item>No hay aislamiento transaccional/snapshot entre la lectura de origen y destino: si hay escrituras
/// concurrentes durante la reconciliación, puede reportarse una discrepancia falsa. Se recomienda ejecutar
/// esta herramienta en una ventana sin escritura (p. ej. tras cortar el tráfico hacia el store viejo) o
/// usar aislamiento snapshot en ambas conexiones.</item>
/// <item>Solo se reconcilian entidades con clave primaria (sin PK no hay orden determinístico posible para
/// el hash) y se excluyen explícitamente los tipos derivados de jerarquías TPH (<c>BaseType != null</c>)
/// para no duplicar el conteo de la tabla raíz; ninguna de las siete entidades de negocio de
/// <c>WorkflowDbContext</c> cae en ninguno de los dos casos.</item>
/// <item>El hash agregado por tabla es un resumen streaming de los hashes por fila (ver
/// <see cref="TableReconciliationResult"/>): confirma igualdad/desigualdad de contenido completo sin
/// cargar la tabla entera en memoria, y ya deja disponible la primera fila divergente sin un segundo
/// paso de búsqueda.</item>
/// </list>
/// </remarks>
public sealed class DataReconciler
{
    private readonly IModel _model;

    public DataReconciler(IModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    /// <summary>
    /// Descubre las tablas físicas reconciliables del modelo, opcionalmente filtradas por nombre.
    /// </summary>
    public IReadOnlyList<TableSchema> DiscoverTables(IReadOnlyCollection<string>? tableFilter = null)
    {
        var schemas = new List<TableSchema>();

        foreach (var entityType in _model.GetEntityTypes())
        {
            // Evita duplicar la tabla raíz de una jerarquía TPH: los tipos derivados comparten tabla con
            // su BaseType, que ya se procesa por separado.
            if (entityType.BaseType is not null)
            {
                continue;
            }

            var tableName = entityType.GetTableName();
            if (tableName is null)
            {
                continue; // Tipo "owned"/sin tabla propia (p. ej. value object mapeado a columnas del dueño).
            }

            if (tableFilter is { Count: > 0 } && !tableFilter.Contains(tableName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey is null)
            {
                // Sin PK no existe un orden determinístico posible para el hash por fila: se omite
                // explícitamente en vez de producir un resultado potencialmente no determinístico.
                continue;
            }

            var columns = entityType.GetProperties()
                .Select(p => p.GetColumnName())
                .Where(c => c is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var keyColumns = primaryKey.Properties
                .Select(p => p.GetColumnName())
                .Where(c => c is not null)
                .Cast<string>()
                .ToList();

            schemas.Add(new TableSchema(entityType.GetSchema(), tableName, columns, keyColumns));
        }

        return schemas.OrderBy(s => s.TableName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Reconcilia todas las tablas descubiertas (o el subconjunto indicado por <paramref name="tableFilter"/>)
    /// entre <paramref name="sourceConnectionString"/> y <paramref name="targetConnectionString"/>.
    /// </summary>
    public async Task<ReconciliationReport> ReconcileAsync(
        string sourceConnectionString,
        string targetConnectionString,
        IReadOnlyCollection<string>? tableFilter = null,
        CancellationToken ct = default)
    {
        var tables = DiscoverTables(tableFilter);
        var results = new List<TableReconciliationResult>(tables.Count);

        foreach (var table in tables)
        {
            results.Add(await ReconcileTableAsync(sourceConnectionString, targetConnectionString, table, ct));
        }

        return new ReconciliationReport(results);
    }

    private static async Task<TableReconciliationResult> ReconcileTableAsync(
        string sourceConnectionString,
        string targetConnectionString,
        TableSchema table,
        CancellationToken ct)
    {
        try
        {
            var sourceCount = await GetCountAsync(sourceConnectionString, table, ct);
            var targetCount = await GetCountAsync(targetConnectionString, table, ct);

            var (sourceHash, targetHash, firstMismatch) =
                await CompareContentAsync(sourceConnectionString, targetConnectionString, table, ct);

            return new TableReconciliationResult(
                table.TableName, sourceCount, targetCount, sourceHash, targetHash, firstMismatch, Error: null);
        }
        catch (Exception ex)
        {
            return new TableReconciliationResult(
                table.TableName, 0, 0, string.Empty, string.Empty, FirstMismatch: null, Error: ex.Message);
        }
    }

    private static async Task<long> GetCountAsync(string connectionString, TableSchema table, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand($"SELECT COUNT_BIG(*) FROM {table.QualifiedName}", connection);
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<(string SourceHash, string TargetHash, RowMismatch? Mismatch)> CompareContentAsync(
        string sourceConnectionString, string targetConnectionString, TableSchema table, CancellationToken ct)
    {
        var sql = BuildSelectSql(table);
        var keyOrdinals = table.KeyColumns
            .Select(k => table.Columns.ToList().FindIndex(c => string.Equals(c, k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        await using var sourceConnection = new SqlConnection(sourceConnectionString);
        await using var targetConnection = new SqlConnection(targetConnectionString);
        await sourceConnection.OpenAsync(ct);
        await targetConnection.OpenAsync(ct);

        await using var sourceCommand = new SqlCommand(sql, sourceConnection);
        await using var targetCommand = new SqlCommand(sql, targetConnection);
        await using var sourceReader = await sourceCommand.ExecuteReaderAsync(ct);
        await using var targetReader = await targetCommand.ExecuteReaderAsync(ct);

        using var sourceAggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var targetAggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        RowMismatch? firstMismatch = null;
        long rowIndex = 0;

        var sourceHasRow = await sourceReader.ReadAsync(ct);
        var targetHasRow = await targetReader.ReadAsync(ct);

        while (sourceHasRow || targetHasRow)
        {
            var sourceRowHash = sourceHasRow ? ComputeRowHash(sourceReader, table.Columns.Count) : "<sin fila en origen>";
            var targetRowHash = targetHasRow ? ComputeRowHash(targetReader, table.Columns.Count) : "<sin fila en destino>";

            if (sourceHasRow)
            {
                sourceAggregate.AppendData(Encoding.UTF8.GetBytes(sourceRowHash));
            }

            if (targetHasRow)
            {
                targetAggregate.AppendData(Encoding.UTF8.GetBytes(targetRowHash));
            }

            if (firstMismatch is null && (!sourceHasRow || !targetHasRow || sourceRowHash != targetRowHash))
            {
                var keyDescription = sourceHasRow
                    ? DescribeKey(sourceReader, table.KeyColumns, keyOrdinals)
                    : DescribeKey(targetReader, table.KeyColumns, keyOrdinals);

                firstMismatch = new RowMismatch(rowIndex, keyDescription, sourceRowHash, targetRowHash);
            }

            rowIndex++;
            sourceHasRow = sourceHasRow && await sourceReader.ReadAsync(ct);
            targetHasRow = targetHasRow && await targetReader.ReadAsync(ct);
        }

        var sourceHashHex = Convert.ToHexString(sourceAggregate.GetHashAndReset());
        var targetHashHex = Convert.ToHexString(targetAggregate.GetHashAndReset());

        return (sourceHashHex, targetHashHex, firstMismatch);
    }

    private static string BuildSelectSql(TableSchema table)
    {
        var columnList = string.Join(", ", table.Columns.Select(TableSchema.Quote));
        var orderList = string.Join(", ", table.KeyColumns.Select(TableSchema.Quote));
        return $"SELECT {columnList} FROM {table.QualifiedName} ORDER BY {orderList}";
    }

    private static string ComputeRowHash(SqlDataReader reader, int columnCount)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < columnCount; i++)
        {
            if (i > 0)
            {
                builder.Append('\u001F'); // Unit separator: no aparece en valores de negocio típicos.
            }

            builder.Append(reader.IsDBNull(i) ? "<NULL>" : CanonicalizeValue(reader.GetValue(i)));
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hashBytes);
    }

    private static string DescribeKey(SqlDataReader reader, IReadOnlyList<string> keyColumns, IReadOnlyList<int> keyOrdinals)
    {
        var parts = new List<string>(keyColumns.Count);
        for (var i = 0; i < keyColumns.Count; i++)
        {
            var ordinal = keyOrdinals[i];
            var value = ordinal >= 0 && !reader.IsDBNull(ordinal)
                ? CanonicalizeValue(reader.GetValue(ordinal))
                : "NULL";
            parts.Add($"{keyColumns[i]}={value}");
        }

        return string.Join(", ", parts);
    }

    private static string CanonicalizeValue(object value) => value switch
    {
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        decimal dec => dec.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}
