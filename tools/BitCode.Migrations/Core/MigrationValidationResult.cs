namespace BitCode.Framework.Tools.Migrations.Core;

public enum RuleSeverity
{
    Warning = 1,
    Error = 2
}

public sealed record MigrationRuleViolation(
    string MigrationName,
    string OperationType,
    string Target,
    RuleSeverity Severity,
    string Message,
    string Remediation);

public sealed class MigrationValidationResult
{
    public MigrationValidationResult(string migrationName, IReadOnlyList<MigrationRuleViolation> violations)
    {
        MigrationName = migrationName;
        Violations = violations;
    }

    public string MigrationName { get; }
    public IReadOnlyList<MigrationRuleViolation> Violations { get; }
    public bool IsValid => Violations.All(v => v.Severity != RuleSeverity.Error);
    public int ErrorCount => Violations.Count(v => v.Severity == RuleSeverity.Error);
    public int WarningCount => Violations.Count(v => v.Severity == RuleSeverity.Warning);
}
