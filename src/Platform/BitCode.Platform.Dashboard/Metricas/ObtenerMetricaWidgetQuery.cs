using BitCode.Framework.Platform.Dashboard.Actors;
using BitCode.Framework.Platform.Dashboard.Widgets;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Dashboard.Metricas;

internal sealed record DashboardWidgetMetricaResponse(
    Guid WidgetId,
    TipoWidgetDashboard Tipo,
    ReportingMetricEstado Estado,
    double? PromedioDuracionSegundos,
    int? CantidadInstanciasFinalizadas,
    string? ErrorMensaje);

/// <summary>
/// Resuelve el valor REAL de un widget del propio dashboard llamando a Reporting (Fase 6, módulo 11) en
/// el momento de la consulta -- ver el <c>remarks</c> del <c>csproj</c> de este proyecto para por qué esto
/// es una llamada HTTP saliente y no un consumo de eventos. Es un <c>IQuery</c> (regla dura 2,
/// <c>docs/convenciones.md</c>: nunca muta) a pesar de hacer una llamada de red -- no hay ningún
/// <c>SaveChangesAsync</c>/<c>IUnitOfWork</c> involucrado, así que no aplica la protección transaccional
/// que esa regla busca preservar.
/// </summary>
/// <remarks>
/// Sin caché en este primer corte (decisión deliberada, documentada honestamente en
/// <c>docs/guia-dashboard.md</c>): cada consulta llama a Reporting sincrónicamente. El trade-off es
/// consciente -- <c>ITenantAwareCache</c> (Shared.Infrastructure.Caching, regla dura 14) es el punto de
/// extensión correcto si un consumidor real reporta que esta llamada síncrona por widget/por request
/// degrada la experiencia del dashboard con muchos widgets simultáneos; agregarlo hoy, sin ese caso real,
/// sería la sobre-ingeniería que la sección 3.2 del Plan Maestro prohíbe (mismo criterio que
/// <c>ListarPromedioDuracionPorDefinicionQuery</c> aplicó a <c>IHotPathQuery</c>). La resiliencia real (que
/// SÍ es obligatoria desde el día uno, F1-26) la aporta <see cref="ReportingHttpMetricSource"/>/
/// <see cref="DashboardReportingHttpClient"/>, no un caché.
/// </remarks>
internal sealed record ObtenerMetricaWidgetQuery(Guid WidgetId) : IQuery<DashboardWidgetMetricaResponse>;

internal sealed class ObtenerMetricaWidgetQueryHandler(
    IReadRepository<DashboardWidget, Guid> repository,
    IDashboardActorContext actorContext,
    IReportingMetricSource metricSource)
    : IRequestHandler<ObtenerMetricaWidgetQuery, Result<DashboardWidgetMetricaResponse>>
{
    public async Task<Result<DashboardWidgetMetricaResponse>> Handle(
        ObtenerMetricaWidgetQuery request, CancellationToken cancellationToken)
    {
        var widget = await repository.GetByIdAsync(request.WidgetId, cancellationToken);
        if (widget is null)
        {
            return Result.Failure<DashboardWidgetMetricaResponse>(Error.NotFound(
                "Dashboard.Widgets.NoEncontrado", $"No existe el widget {request.WidgetId}."));
        }

        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure<DashboardWidgetMetricaResponse>(Error.Forbidden(
                "Dashboard.Widgets.ActorNoResuelto", "No se pudo resolver el actor autenticado."));
        }

        // Mismo chequeo de ownership que ObtenerWidgetQuery -- la métrica de un widget es tan personal
        // como el widget mismo, nunca visible para otro usuario aunque tenga el permiso genérico.
        if (widget.UserId != actorUserId.Value)
        {
            return Result.Failure<DashboardWidgetMetricaResponse>(Error.Forbidden(
                "Dashboard.Widgets.NoAutorizado", "Solo el dueño del widget puede ver su métrica."));
        }

        switch (widget.Tipo)
        {
            case TipoWidgetDashboard.PromedioDuracionWorkflowPorDefinicion:
                // WorkflowDefinitionId es obligatorio para este tipo (AgregarWidgetCommandValidator) --
                // el operador ! es seguro acá, nunca un dato realmente ausente para este Tipo.
                var metrica = await metricSource.ObtenerPromedioDuracionPorDefinicionAsync(
                    widget.WorkflowDefinitionId!.Value, cancellationToken);

                return Result.Success(new DashboardWidgetMetricaResponse(
                    widget.Id, widget.Tipo, metrica.Estado, metrica.PromedioDuracionSegundos,
                    metrica.CantidadInstanciasFinalizadas, metrica.ErrorMensaje));

            default:
                // No alcanzable con el único valor de TipoWidgetDashboard que existe hoy -- defensivo
                // ante un valor de enum futuro que agregue un tipo sin implementar todavía su resolución.
                return Result.Failure<DashboardWidgetMetricaResponse>(Error.Validation(
                    "Dashboard.Widgets.TipoNoSoportado", $"El tipo de widget '{widget.Tipo}' no tiene una métrica implementada."));
        }
    }
}
