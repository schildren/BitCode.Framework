namespace BitCode.Framework.Platform.Dashboard.Widgets;

internal sealed record DashboardWidgetResponse(
    Guid Id, TipoWidgetDashboard Tipo, string Titulo, Guid? WorkflowDefinitionId, int Orden);
