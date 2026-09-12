using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BitCode.Framework.Tools.Migrations.Core;

/// <summary>
/// Generador de scripts SQL de migración (idempotentes) para DBAs o pipelines de despliegue.
/// </summary>
public sealed class MigrationScriptGenerator
{
    private readonly DbContext _context;

    public MigrationScriptGenerator(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Genera un script SQL idempotente desde una migración inicial (o desde el inicio si es null)
    /// hasta una migración final (o la última si es null).
    /// </summary>
    public string GenerateIdempotentScript(string? fromMigration = null, string? toMigration = null)
    {
        var migrator = _context.GetService<IMigrator>();
        return migrator.GenerateScript(fromMigration, toMigration, MigrationsSqlGenerationOptions.Idempotent);
    }
}
