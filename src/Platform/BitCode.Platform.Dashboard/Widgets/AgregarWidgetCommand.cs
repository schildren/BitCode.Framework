using BitCode.Framework.Platform.Dashboard.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Dashboard.Widgets;

/// <summary>
/// Agrega un widget al PROPIO dashboard del actor autenticado -- nunca recibe un <c>UserId</c> del
/// llamador (ver <c>DashboardWidget.UserId</c>). No auditado vía <c>IAuditWriter</c> (F2-15): mismo
/// criterio que <c>MarcarComoLeidaCommand</c> de Task Inbox -- configurar el propio dashboard es un
/// detalle de experiencia personal sin valor de auditoría/cumplimiento, a diferencia de una operación
/// administrativa sensible (alta de un conector, creación de un documento).
/// </summary>
internal sealed record AgregarWidgetCommand(TipoWidgetDashboard Tipo, string Titulo, Guid? WorkflowDefinitionId)
    : ICommand<Guid>;

internal sealed class AgregarWidgetCommandValidator : AbstractValidator<AgregarWidgetCommand>
{
    public AgregarWidgetCommandValidator()
    {
        RuleFor(c => c.Titulo).NotEmpty().MaximumLength(256);
        RuleFor(c => c.Tipo).IsInEnum();

        // El único tipo de este primer corte necesita saber SOBRE QUÉ definición de workflow calcular el
        // promedio -- sin este dato el widget nunca podría resolver ninguna métrica real (ver
        // Metricas.ObtenerMetricaWidgetQuery).
        RuleFor(c => c.WorkflowDefinitionId).NotEmpty()
            .When(c => c.Tipo == TipoWidgetDashboard.PromedioDuracionWorkflowPorDefinicion)
            .WithMessage("WorkflowDefinitionId es obligatorio para un widget de tipo PromedioDuracionWorkflowPorDefinicion.");
    }
}

/// <summary>
/// No llama <c>IUnitOfWork.SaveChangesAsync</c> explícitamente (regla dura 1, <c>docs/convenciones.md</c>):
/// <c>TransactionBehavior</c> lo hace al final del pipeline.
/// </summary>
internal sealed class AgregarWidgetCommandHandler(
    IRepository<DashboardWidget, Guid> repository,
    IReadRepository<DashboardWidget, Guid> readRepository,
    IDashboardActorContext actorContext)
    : IRequestHandler<AgregarWidgetCommand, Result<Guid>>
{
    /// <summary>Límite defensivo -- un dashboard con más widgets que esto ya dejó de ser útil como
    /// vista rápida (el propósito mismo de un dashboard). Sin este límite, nada impedía que un cliente
    /// mal comportado agregara miles de widgets al mismo dashboard.</summary>
    private const int MaximoWidgetsPorUsuario = 20;

    public async Task<Result<Guid>> Handle(AgregarWidgetCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure<Guid>(Error.Forbidden(
                "Dashboard.Widgets.ActorNoResuelto", "No se pudo resolver el actor autenticado."));
        }

        var widgetsExistentes = await readRepository.ListAsync(
            new WidgetsDeUsuarioSpecification(actorUserId.Value), cancellationToken);

        if (widgetsExistentes.Count >= MaximoWidgetsPorUsuario)
        {
            return Result.Failure<Guid>(Error.Validation(
                "Dashboard.Widgets.LimiteExcedido",
                $"No se pueden tener más de {MaximoWidgetsPorUsuario} widgets en el propio dashboard."));
        }

        var siguienteOrden = widgetsExistentes.Count == 0 ? 0 : widgetsExistentes.Max(w => w.Orden) + 1;

        var widget = new DashboardWidget(
            Guid.NewGuid(), actorUserId.Value, request.Tipo, request.Titulo, request.WorkflowDefinitionId, siguienteOrden);

        await repository.AddAsync(widget, cancellationToken);

        return widget.Id;
    }
}
