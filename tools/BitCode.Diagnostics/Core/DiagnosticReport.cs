namespace BitCode.Framework.Tools.Diagnostics.Core;

/// <summary>
/// Reporte consolidado de una ejecución de diagnóstico.
/// </summary>
public sealed class DiagnosticReport
{
    public DateTimeOffset ExecutedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public long ElapsedMilliseconds { get; set; }
    public List<DiagnosticItem> Items { get; set; } = new();

    public int TotalChecks => Items.Count;
    public int Passed => Items.Count(i => i.Severity == DiagnosticSeverity.Success);
    public int Warnings => Items.Count(i => i.Severity == DiagnosticSeverity.Warning);
    public int Failures => Items.Count(i => i.Severity == DiagnosticSeverity.Error);

    public bool HasErrors => Failures > 0;
    public bool HasWarnings => Warnings > 0;
    public bool IsHealthy => !HasErrors && !HasWarnings;

    public void Add(DiagnosticItem item)
    {
        Items.Add(item);
    }

    public void AddRange(IEnumerable<DiagnosticItem> items)
    {
        Items.AddRange(items);
    }
}
