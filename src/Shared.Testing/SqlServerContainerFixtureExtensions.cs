using Microsoft.Data.SqlClient;

namespace BitCode.Framework.Shared.Testing;

public static class SqlServerContainerFixtureExtensions
{
    /// <summary>
    /// Construye un connection string que apunta a una base de datos exclusiva (nombre derivado
    /// del nombre del test + un Guid) dentro del mismo contenedor, para que tests en paralelo o
    /// consecutivos no compartan estado.
    /// </summary>
    public static string BuildIsolatedConnectionString(
        this SqlServerContainerFixture fixture,
        string databasePrefix,
        string testName)
    {
        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            InitialCatalog = $"{databasePrefix}_{testName}_{Guid.NewGuid():N}",
        };

        return builder.ConnectionString;
    }
}
