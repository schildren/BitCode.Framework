namespace BitCode.Framework.Tools.Diagnostics.Core;

/// <summary>
/// Categoría de inspección de diagnóstico (F8-12).
/// </summary>
public enum DiagnosticCategory
{
    /// <summary>
    /// Herramientas de desarrollo, compiladores y SDKs (dotnet, node, docker, git).
    /// </summary>
    Tools,

    /// <summary>
    /// Configuración de la aplicación, variables de entorno y archivos requeridos.
    /// </summary>
    Configuration,

    /// <summary>
    /// Conectividad y salud de servicios externos (SQL Server, Redis, Kafka, OTel, Jaeger).
    /// </summary>
    Connectivity
}
