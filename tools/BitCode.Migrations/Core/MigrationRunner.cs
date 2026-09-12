using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BitCode.Framework.Tools.Migrations.Core;

public sealed record MigrationStatusInfo(
    string DbContextName,
    string DatabaseName,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    IReadOnlyList<string> AllMigrations)
{
    public bool HasPendingMigrations => PendingMigrations.Count > 0;
    public string? LatestAppliedMigration => AppliedMigrations.LastOrDefault();
}

/// <summary>
/// Servicio para consultar estado, aplicar forward y ejecutar rollback de migraciones EF Core.
/// </summary>
public sealed class MigrationRunner
{
    private readonly DbContext _context;

    public MigrationRunner(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Obtiene el estado actual de las migraciones en la base de datos de destino.
    /// </summary>
    public async Task<MigrationStatusInfo> GetStatusAsync(CancellationToken ct = default)
    {
        var database = _context.Database;
        var dbName = database.GetDbConnection().Database;
        var contextName = _context.GetType().Name;

        var appliedMigrations = (await database.GetAppliedMigrationsAsync(ct)).ToList();
        var pendingMigrations = (await database.GetPendingMigrationsAsync(ct)).ToList();
        var allMigrations = database.GetMigrations().ToList();

        return new MigrationStatusInfo(
            DbContextName: contextName,
            DatabaseName: dbName,
            AppliedMigrations: appliedMigrations,
            PendingMigrations: pendingMigrations,
            AllMigrations: allMigrations);
    }

    /// <summary>
    /// Aplica las migraciones hacia adelante (Forward rollout) hasta la migración objetivo o la última disponible.
    /// </summary>
    public async Task<IReadOnlyList<string>> MigrateForwardAsync(string? targetMigration = null, CancellationToken ct = default)
    {
        var migrator = _context.GetService<IMigrator>();
        var statusBefore = await GetStatusAsync(ct);

        await migrator.MigrateAsync(targetMigration, ct);

        var statusAfter = await GetStatusAsync(ct);
        var newlyApplied = statusAfter.AppliedMigrations
            .Except(statusBefore.AppliedMigrations)
            .ToList();

        return newlyApplied;
    }

    /// <summary>
    /// Ejecuta la reversión controlada (Rollback ensayado) hacia la migración objetivo (o "0" para revertir todas).
    /// </summary>
    public async Task<IReadOnlyList<string>> RollbackAsync(string targetMigration, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetMigration, nameof(targetMigration));

        var migrator = _context.GetService<IMigrator>();
        var statusBefore = await GetStatusAsync(ct);

        await migrator.MigrateAsync(targetMigration, ct);

        var statusAfter = await GetStatusAsync(ct);
        var rolledBack = statusBefore.AppliedMigrations
            .Except(statusAfter.AppliedMigrations)
            .ToList();

        return rolledBack;
    }
}
