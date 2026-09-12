namespace BitCode.Framework.Platform.Dashboard.Metricas;

public enum ReportingMetricEstado
{
    /// <summary>La llamada a Reporting tuvo éxito -- <see cref="ReportingMetricResultado.PromedioDuracionSegundos"/>
    /// puede seguir siendo <see langword="null"/> si todavía no hay ninguna instancia finalizada para la
    /// definición consultada (dato de negocio legítimo, no un fallo).</summary>
    Disponible = 0,

    /// <summary>Reporting no respondió, respondió con error, o la respuesta no se pudo interpretar --
    /// nunca una excepción no controlada escapando al cliente del dashboard (mismo criterio de
    /// clasificación que <c>IntegrationSendOutcome</c>, Fase 6, módulo 9).</summary>
    NoDisponible = 1,
}

/// <summary>
/// Resultado de resolver la métrica de UN widget contra la API pública de Reporting (Fase 6, módulo 11)
/// -- ver <c>IReportingMetricSource</c>.
/// </summary>
public sealed record ReportingMetricResultado(
    ReportingMetricEstado Estado,
    double? PromedioDuracionSegundos,
    int? CantidadInstanciasFinalizadas,
    string? ErrorMensaje)
{
    public static ReportingMetricResultado Disponible(double? promedioDuracionSegundos, int cantidadInstanciasFinalizadas) =>
        new(ReportingMetricEstado.Disponible, promedioDuracionSegundos, cantidadInstanciasFinalizadas, null);

    public static ReportingMetricResultado NoDisponible(string errorMensaje) =>
        new(ReportingMetricEstado.NoDisponible, null, null, errorMensaje);
}
