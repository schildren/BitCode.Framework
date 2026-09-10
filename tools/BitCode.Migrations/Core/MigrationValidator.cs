using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace BitCode.Framework.Tools.Migrations.Core;

/// <summary>
/// Validador estático de operaciones de migración EF Core según el principio Expand-and-Contract
/// y los guardrails del Plan Maestro de BitCode (secciones 11 y 13).
/// </summary>
public sealed class MigrationValidator
{
    private readonly bool _allowDestructive;

    public MigrationValidator(bool allowDestructive = false)
    {
        _allowDestructive = allowDestructive;
    }

    /// <summary>
    /// Valida un conjunto de operaciones de migración para una migración con nombre dado.
    /// </summary>
    public MigrationValidationResult ValidateOperations(string migrationName, IReadOnlyList<MigrationOperation> operations)
    {
        var violations = new List<MigrationRuleViolation>();

        foreach (var op in operations)
        {
            switch (op)
            {
                case DropTableOperation dropTable:
                    violations.Add(new MigrationRuleViolation(
                        MigrationName: migrationName,
                        OperationType: nameof(DropTableOperation),
                        Target: dropTable.Name,
                        Severity: _allowDestructive ? RuleSeverity.Warning : RuleSeverity.Error,
                        Message: $"Eliminación de tabla detectada ('{dropTable.Name}'). Viola la regla expand-and-contract en despliegues zero-downtime.",
                        Remediation: "No elimine tablas activas. Aplique primero la fase de transición (desacoplar consumidores) y reserve DROP TABLE para la fase Contract autorizada explícitamente con --allow-destructive."));
                    break;

                case DropColumnOperation dropColumn:
                    violations.Add(new MigrationRuleViolation(
                        MigrationName: migrationName,
                        OperationType: nameof(DropColumnOperation),
                        Target: $"{dropColumn.Table}.{dropColumn.Name}",
                        Severity: _allowDestructive ? RuleSeverity.Warning : RuleSeverity.Error,
                        Message: $"Eliminación de columna detectada ('{dropColumn.Table}.{dropColumn.Name}'). Réplicas previas de la aplicación fallarán al consultar la tabla durante rolling updates.",
                        Remediation: "Siga expand-and-contract: marque la propiedad obsoleta en el código, deje de leerla/escribirla en producción, y elimine la columna física solo tras confirmar que no existen consumidores activos."));
                    break;

                case RenameTableOperation renameTable:
                    violations.Add(new MigrationRuleViolation(
                        MigrationName: migrationName,
                        OperationType: nameof(RenameTableOperation),
                        Target: $"{renameTable.Name} -> {renameTable.NewName}",
                        Severity: _allowDestructive ? RuleSeverity.Warning : RuleSeverity.Error,
                        Message: $"Renombrado directo de tabla ('{renameTable.Name}' a '{renameTable.NewName}') rompe inmediatamente réplicas anteriores de la aplicación.",
                        Remediation: "Cree la nueva tabla (Expand), sincronice datos y redirija tráfico gradualmente, luego retire la tabla antigua (Contract)."));
                    break;

                case RenameColumnOperation renameColumn:
                    violations.Add(new MigrationRuleViolation(
                        MigrationName: migrationName,
                        OperationType: nameof(RenameColumnOperation),
                        Target: $"{renameColumn.Table}.{renameColumn.Name} -> {renameColumn.NewName}",
                        Severity: _allowDestructive ? RuleSeverity.Warning : RuleSeverity.Error,
                        Message: $"Renombrado directo de columna ('{renameColumn.Table}.{renameColumn.Name}' a '{renameColumn.NewName}') rompe réplicas anteriores en producción.",
                        Remediation: "Agregue la nueva columna (Expand), mantenga sincronización o compatibilidad en código, y retire la columna anterior en una release posterior (Contract)."));
                    break;

                case AddColumnOperation addColumn:
                    if (!addColumn.IsNullable && addColumn.DefaultValue == null && string.IsNullOrEmpty(addColumn.DefaultValueSql))
                    {
                        violations.Add(new MigrationRuleViolation(
                            MigrationName: migrationName,
                            OperationType: nameof(AddColumnOperation),
                            Target: $"{addColumn.Table}.{addColumn.Name}",
                            Severity: RuleSeverity.Error,
                            Message: $"Columna NOT NULL agregada sin valor DEFAULT ('{addColumn.Table}.{addColumn.Name}'). Romperá inserciones de réplicas en versión previa y fallará si la tabla contiene filas.",
                            Remediation: "Defina la columna como Nullable durante la fase Expand, o proporcione un DefaultValue / DefaultValueSql explícito compatible con la versión en ejecución."));
                    }
                    else if (!addColumn.IsNullable && (addColumn.DefaultValue != null || !string.IsNullOrEmpty(addColumn.DefaultValueSql)))
                    {
                        violations.Add(new MigrationRuleViolation(
                            MigrationName: migrationName,
                            OperationType: nameof(AddColumnOperation),
                            Target: $"{addColumn.Table}.{addColumn.Name}",
                            Severity: RuleSeverity.Warning,
                            Message: $"Columna NOT NULL con DEFAULT ('{addColumn.Table}.{addColumn.Name}'). Segura para filas existentes, pero las réplicas anteriores no suministrarán este valor en inserciones nuevas.",
                            Remediation: "Verifique que el valor por defecto configurado sea semánticamente inocuo para la lógica de la versión anterior durante el rolling deployment."));
                    }
                    break;

                case AlterColumnOperation alterColumn:
                    violations.Add(new MigrationRuleViolation(
                        MigrationName: migrationName,
                        OperationType: nameof(AlterColumnOperation),
                        Target: $"{alterColumn.Table}.{alterColumn.Name}",
                        Severity: RuleSeverity.Warning,
                        Message: $"Modificación de columna detectada ('{alterColumn.Table}.{alterColumn.Name}').",
                        Remediation: "Verifique que el cambio de tipo de datos o longitud sea backward-compatible y no genere bloqueos prolongados o truncado."));
                    break;
            }
        }

        return new MigrationValidationResult(migrationName, violations);
    }

    /// <summary>
    /// Valida una instancia concreta de <see cref="Migration"/>.
    /// </summary>
    public MigrationValidationResult ValidateMigration(Migration migration)
    {
        var migrationName = migration.GetType().Name;
        return ValidateOperations(migrationName, migration.UpOperations);
    }
}
