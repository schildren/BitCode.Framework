using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace BitCode.Framework.Shared.Infrastructure.BackgroundJobs;

/// <summary>
/// Aplica el esquema <c>QRTZ_*</c> (<c>Schema/quartz-sqlserver-schema.sql</c>, adaptado del script
/// oficial de Quartz.NET) contra un SQL Server real, de forma idempotente: si
/// <c>QRTZ_JOB_DETAILS</c> ya existe, no hace nada.
/// </summary>
/// <remarks>
/// Este helper es deliberadamente el ÚNICO lugar del framework que decide "correr o no correr" el
/// script — el archivo <c>.sql</c> en sí no incluye ninguna comprobación de idempotencia (SQL
/// Server no soporta <c>CREATE TABLE IF NOT EXISTS</c>), ver el comentario de cabecera de ese
/// archivo.
/// <para>
/// Dos usos previstos, ninguno de ellos "ejecutar en cada arranque de cada pod":
/// </para>
/// <list type="bullet">
/// <item>Pruebas de integración con Testcontainers (mismo patrón que
/// <c>SqlServerContainerFixture</c>/<c>EnsureCreatedAsync</c> ya usado en el resto del
/// repositorio para levantar esquema contra SQL Server real).</item>
/// <item>Un paso explícito del pipeline de despliegue de un consumidor real (job de migración
/// dedicado, ejecutado una vez antes del rollout de los pods que corren el scheduler) — NO debe
/// invocarse desde <c>Program.cs</c> de cada réplica: N pods arrancando en paralelo y llamando
/// este método al mismo tiempo competirían por crear el mismo esquema (mismo riesgo ya señalado
/// para <c>EnsureCreatedAsync</c> en `docs/auditoria-estado-runtime-f4-03.md`, sección 5). Ver
/// `docs/guia-quartz-ha.md` para el detalle operativo recomendado.</item>
/// </list>
/// </remarks>
public static class QuartzSqlServerSchemaInitializer
{
    private const string EmbeddedResourceName =
        "Shared.Infrastructure.BackgroundJobs.Schema.quartz-sqlserver-schema.sql";

    private static readonly Regex BatchSeparator =
        new(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Crea las tablas <c>QRTZ_*</c> si todavía no existen en la base de datos apuntada por
    /// <paramref name="connectionString"/>. No hace nada (ni falla) si ya existen — seguro de
    /// invocar más de una vez.
    /// </summary>
    public static async Task ApplySchemaIfMissingAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        if (await SchemaExistsAsync(connection, cancellationToken))
        {
            return;
        }

        var script = ReadEmbeddedScript();

        foreach (var batch in BatchSeparator.Split(script))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandTimeout = 60;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> SchemaExistsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT OBJECT_ID(N'[dbo].[QRTZ_JOB_DETAILS]', N'U')";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    private static string ReadEmbeddedScript()
    {
        var assembly = typeof(QuartzSqlServerSchemaInitializer).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"No se encontró el recurso embebido '{EmbeddedResourceName}' en " +
                $"'{assembly.FullName}' — verificar que quartz-sqlserver-schema.sql siga declarado " +
                "como EmbeddedResource en Shared.Infrastructure.BackgroundJobs.csproj.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
