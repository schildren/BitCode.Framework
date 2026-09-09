using BitCode.Framework.Platform.Workflow.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

/// <summary>Un estado a crear dentro de la versión en borrador (referenciado por <see cref="Codigo"/>
/// desde <see cref="WorkflowTransitionInput"/> -- las transiciones no conocen el <c>Guid</c> real hasta
/// que el handler los genera).</summary>
internal sealed record WorkflowStateInput(
    string Codigo, string Nombre, bool EsInicial, bool EsFinal, bool RequiereTarea, string? TituloTarea,
    Guid? AsignadoPorDefectoUserId, int? SlaMinutos, Guid? EscalarAUserId);

internal sealed record WorkflowTransitionInput(
    string DesdeCodigo, string HaciaCodigo, string Accion, string? ReglaExpresion, int Orden);

/// <summary>
/// Crea una nueva <see cref="WorkflowVersion"/> en estado <see cref="WorkflowVersionEstado.Borrador"/>
/// junto con su grafo completo de <see cref="WorkflowState"/>/<see cref="WorkflowTransition"/> -- mismo
/// espíritu que <c>CrearCatalogoVersionCommand</c> (Catalogs, Fase 6 módulo 3): publicarla es un paso
/// explícito y posterior (<c>PublicarWorkflowVersionCommand</c>), donde recién se valida la forma del
/// grafo (un único estado inicial, al menos un estado final, asignación obligatoria en todo estado con
/// tarea). Implementa <see cref="IIdempotentCommand"/> (F1-22).
/// </summary>
internal sealed record CrearWorkflowVersionCommand(
    Guid WorkflowDefinitionId, IReadOnlyList<WorkflowStateInput> Estados, IReadOnlyList<WorkflowTransitionInput> Transiciones)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearWorkflowVersionCommandValidator : AbstractValidator<CrearWorkflowVersionCommand>
{
    public CrearWorkflowVersionCommandValidator()
    {
        RuleFor(c => c.WorkflowDefinitionId).NotEmpty();
        RuleFor(c => c.Estados).NotEmpty();
        RuleForEach(c => c.Estados).ChildRules(estado =>
        {
            estado.RuleFor(e => e.Codigo).NotEmpty().MaximumLength(64);
            estado.RuleFor(e => e.Nombre).NotEmpty().MaximumLength(200);
        });
        RuleForEach(c => c.Transiciones).ChildRules(transicion =>
        {
            transicion.RuleFor(t => t.DesdeCodigo).NotEmpty();
            transicion.RuleFor(t => t.HaciaCodigo).NotEmpty();
            transicion.RuleFor(t => t.Accion).NotEmpty().MaximumLength(64);
        });
    }
}

internal sealed class CrearWorkflowVersionCommandHandler(
    IReadRepository<WorkflowDefinition, Guid> definicionRepository,
    IRepository<WorkflowVersion, Guid> versionRepository,
    IRepository<WorkflowState, Guid> stateRepository,
    IRepository<WorkflowTransition, Guid> transitionRepository,
    IAuditWriter auditWriter,
    IWorkflowActorContext actorContext)
    : IRequestHandler<CrearWorkflowVersionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearWorkflowVersionCommand request, CancellationToken cancellationToken)
    {
        var definicion = await definicionRepository.GetByIdAsync(request.WorkflowDefinitionId, cancellationToken);
        if (definicion is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Workflow.Definiciones.NoEncontrada", $"No existe el workflow {request.WorkflowDefinitionId}."));
        }

        var codigosDuplicados = request.Estados.GroupBy(e => e.Codigo).Any(g => g.Count() > 1);
        if (codigosDuplicados)
        {
            return Result.Failure<Guid>(Error.Validation(
                "Workflow.Versiones.CodigosDeEstadoDuplicados", "Los códigos de estado deben ser únicos dentro de la versión."));
        }

        var numero = await versionRepository.CountAsync(
            new VersionesDeWorkflowSpecification(request.WorkflowDefinitionId), cancellationToken) + 1;

        var version = new WorkflowVersion(Guid.NewGuid(), definicion.Id, numero);
        await versionRepository.AddAsync(version, cancellationToken);

        var idsPorCodigo = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var estadoInput in request.Estados)
        {
            var estado = new WorkflowState(
                Guid.NewGuid(), version.Id, estadoInput.Codigo, estadoInput.Nombre, estadoInput.EsInicial,
                estadoInput.EsFinal, estadoInput.RequiereTarea, estadoInput.TituloTarea,
                estadoInput.AsignadoPorDefectoUserId, estadoInput.SlaMinutos, estadoInput.EscalarAUserId);
            await stateRepository.AddAsync(estado, cancellationToken);
            idsPorCodigo[estadoInput.Codigo] = estado.Id;
        }

        foreach (var transicionInput in request.Transiciones)
        {
            if (!idsPorCodigo.TryGetValue(transicionInput.DesdeCodigo, out var desdeId) ||
                !idsPorCodigo.TryGetValue(transicionInput.HaciaCodigo, out var haciaId))
            {
                return Result.Failure<Guid>(Error.Validation(
                    "Workflow.Versiones.TransicionConCodigoInexistente",
                    $"La transición '{transicionInput.Accion}' referencia un código de estado que no existe en esta versión."));
            }

            await transitionRepository.AddAsync(
                new WorkflowTransition(
                    Guid.NewGuid(), version.Id, desdeId, haciaId, transicionInput.Accion,
                    transicionInput.ReglaExpresion, transicionInput.Orden),
                cancellationToken);
        }

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "workflow.versiones.crear",
            resource: new AuditResource("workflow.versiones", version.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?>
            {
                ["workflowDefinitionId"] = definicion.Id.ToString(),
                ["numero"] = numero.ToString(),
                ["cantidadEstados"] = request.Estados.Count.ToString(),
                ["cantidadTransiciones"] = request.Transiciones.Count.ToString(),
            });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return version.Id;
    }
}
