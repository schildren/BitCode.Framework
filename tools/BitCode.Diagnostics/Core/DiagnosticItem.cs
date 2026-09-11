namespace BitCode.Framework.Tools.Diagnostics.Core;

/// <summary>
/// Representa el resultado individual de una comprobación de diagnóstico accionable.
/// </summary>
public sealed class DiagnosticItem
{
    public DiagnosticCategory Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public DiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? Remediation { get; set; }

    public static DiagnosticItem Success(DiagnosticCategory category, string name, string message, string? details = null)
    {
        return new DiagnosticItem
        {
            Category = category,
            Name = name,
            Severity = DiagnosticSeverity.Success,
            Message = message,
            Details = details
        };
    }

    public static DiagnosticItem Warning(DiagnosticCategory category, string name, string message, string? details = null, string? remediation = null)
    {
        return new DiagnosticItem
        {
            Category = category,
            Name = name,
            Severity = DiagnosticSeverity.Warning,
            Message = message,
            Details = details,
            Remediation = remediation
        };
    }

    public static DiagnosticItem Error(DiagnosticCategory category, string name, string message, string? details = null, string? remediation = null)
    {
        return new DiagnosticItem
        {
            Category = category,
            Name = name,
            Severity = DiagnosticSeverity.Error,
            Message = message,
            Details = details,
            Remediation = remediation
        };
    }
}
