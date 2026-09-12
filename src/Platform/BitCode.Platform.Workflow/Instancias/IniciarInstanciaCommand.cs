using BitCode.Framework.Platform.Workflow.Actors;
using BitCode.Framework.Platform.Workflow.Definiciones;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>
/// Inicia una nueva <see cref="WorkflowInstance"/> a partir de la última <see cref="WorkflowVersion"/>
/// PUBLICADA de un <see cref="WorkflowDefinition"/> -- nunca de una versión en borrador. La instancia
/// arranca en el estado inicial del grafo (<see cref="WorkflowState.EsInicial"/>): si ese estado requiere
/// tarea humana, la instancia se detiene ahí con su primera <see cref="WorkflowTask"/> creada; si no, el
/// motor (<see cref="IWorkflowEngine"/>) avanza automáticamente hasta detenerse en el primer estado que
/// requiere tarea o alcanzar un estado final. Implementa <see cref="IIdempotentCommand"/> (F1-22).
/// </summary>
internal sealed record IniciarInstanciaCommand(Guid WorkflowDefinitionId, Dictionary<string, string> Variables)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class IniciarInstanciaCommandValidator : AbstractValidator<IniciarInstanciaCommand>
{
    public IniciarInstanciaCommandValidator() => RuleFor(c => c.WorkflowDefinitionId).NotEmpty();
}

internal sealed class IniciarInstanciaCommandHandler(
    IReadRepository<WorkflowVersion, Guid> versionRepository,
    IReadRepository<WorkflowState, Guid> stateRepository,
    IRepository<WorkflowInstance, Guid> instanceRepository,
    IRepository<WorkflowTask, Guid> taskRepository,
    IRepository<WorkflowHistorial, Guid> historialRepository,
    IWorkflowEngine engine,
    IAuditWriter auditWriter,
    IWorkflowActorContext actorContext)
    : IRequestHandler<IniciarInstanciaCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(IniciarInstanciaCommand request, CancellationToken cancellationToken)
    {
        var version = (await versionRepository.ListAsync(
            new UltimaVersionPublicadaSpecification(request.WorkflowDefinitionId), cancellationToken)).FirstOrDefault();
        if (version is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Workflow.Instancias.SinVersionPublicada",
                $"El workflow {request.WorkflowDefinitionId} no tiene ninguna versión publicada."));
        }

        var estados = await stateRepository.ListAsync(new EstadosDeVersionSpecification(version.Id), cancellationToken);
        var estadoInicial = estados.SingleOrDefault(e => e.EsInicial);
        if (estadoInicial is null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "Workflow.Instancias.GrafoInvalido", "La versión publicada no tiene un estado inicial válido."));
        }

        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure<Guid>(new Error(
                "Workflow.Instancias.ActorNoResuelto",
                "No se pudo resolver el actor autenticado que inicia la instancia.", ErrorType.Forbidden));
        }

        var instancia = new WorkflowInstance(
            Guid.NewGuid(), version.WorkflowDefinitionId, version.Id, estadoInicial.Id, actorUserId.Value, request.Variables);
        await instanceRepository.AddAsync(instancia, cancellationToken);

        await historialRepository.AddAsync(
            new WorkflowHistorial(Guid.NewGuid(), instancia.Id, "InstanciaIniciada",
                $"Instancia iniciada en estado '{estadoInicial.Nombre}'.", actorUserId.Value),
            cancellationToken);

        if (estadoInicial.RequiereTarea)
        {
            var tarea = new WorkflowTask(
                Guid.NewGuid(), instancia.Id, estadoInicial.Id, estadoInicial.TituloTarea ?? estadoInicial.Nombre,
                estadoInicial.AsignadoPorDefectoUserId!.Value,
                estadoInicial.SlaMinutos.HasValue ? DateTime.UtcNow.AddMinutes(estadoInicial.SlaMinutos.Value) : null);
            await taskRepository.AddAsync(tarea, cancellationToken);

            await historialRepository.AddAsync(
                new WorkflowHistorial(Guid.NewGuid(), instancia.Id, "TareaCreada", $"Tarea '{tarea.Titulo}' asignada.", actorUserId: null),
                cancellationToken);
        }
        else
        {
            var avanceResult = await engine.AvanzarAsync(instancia, accion: null, cancellationToken);
            if (avanceResult.IsFailure)
            {
                return Result.Failure<Guid>(avanceResult.Error);
            }
        }

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "workflow.instancias.iniciar",
            resource: new AuditResource("workflow.instancias", instancia.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?>
            {
                ["workflowDefinitionId"] = version.WorkflowDefinitionId.ToString(),
                ["workflowVersionId"] = version.Id.ToString(),
            });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return instancia.Id;
    }
}
