namespace BitCode.Framework.Platform.Dashboard.Widgets;

/// <summary>
/// Tipos de widget soportados. DELIBERADAMENTE un único tipo REAL en este primer corte (Plan Maestro,
/// Fase 6, módulo 12: "widgets" -- "al menos un tipo de widget de referencia real, consumiendo datos
/// reales", ver <c>docs/guia-dashboard.md</c>) -- no un catálogo genérico de widgets sin datos detrás.
/// </summary>
public enum TipoWidgetDashboard
{
    /// <summary>Tiempo promedio de resolución de instancias de Workflow para UNA definición concreta
    /// (<see cref="DashboardWidget.WorkflowDefinitionId"/>, obligatorio para este tipo) -- resuelto en el
    /// momento de consultar el dashboard llamando a la API pública de Reporting (Fase 6, módulo 11) vía
    /// <see cref="Metricas.IReportingMetricSource"/>, nunca almacenado ni pre-calculado por este módulo.</summary>
    PromedioDuracionWorkflowPorDefinicion = 0,
}
