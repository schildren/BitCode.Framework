namespace BitCode.Framework.Tools.Diagnostics.Core;

/// <summary>
/// Severidad o resultado del elemento de diagnóstico.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Verificación exitosa.
    /// </summary>
    Success = 0,

    /// <summary>
    /// Advertencia no bloqueante pero con impacto potencial.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Error bloqueante que impide el correcto desarrollo o ejecución.
    /// </summary>
    Error = 2
}
