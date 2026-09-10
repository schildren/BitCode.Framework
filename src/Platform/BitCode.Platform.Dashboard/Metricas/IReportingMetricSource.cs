namespace BitCode.Framework.Platform.Dashboard.Metricas;

/// <summary>
/// Resuelve el valor real de un widget de tipo
/// <see cref="Widgets.TipoWidgetDashboard.PromedioDuracionWorkflowPorDefinicion"/> consultando a Reporting
/// (Fase 6, módulo 11) EN EL MOMENTO de la consulta -- nunca pre-calculado ni almacenado por este módulo
/// (ver el <c>remarks</c> del <c>csproj</c> de este proyecto para por qué esto es una llamada HTTP a un
/// sistema externo y no un consumo de eventos de integración). La única implementación real de este
/// primer corte es <see cref="ReportingHttpMetricSource"/>; la interfaz existe para que un test pueda
/// sustituirla por un doble de prueba sin necesitar un servidor HTTP real cuando el escenario no lo
/// amerita (los tests de integración de referencia SÍ usan un servidor HTTP real, ver
/// <c>docs/guia-dashboard.md</c>).
/// </summary>
public interface IReportingMetricSource
{
    Task<ReportingMetricResultado> ObtenerPromedioDuracionPorDefinicionAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default);
}
