using BitCode.Framework.Tools.Diagnostics.Core;

namespace BitCode.Framework.Tools.Diagnostics.Checkers;

/// <summary>
/// Contrato común para componentes verificadores de diagnóstico.
/// </summary>
public interface IDiagnosticChecker
{
    DiagnosticCategory Category { get; }
    Task<IReadOnlyList<DiagnosticItem>> RunChecksAsync(CancellationToken cancellationToken = default);
}
