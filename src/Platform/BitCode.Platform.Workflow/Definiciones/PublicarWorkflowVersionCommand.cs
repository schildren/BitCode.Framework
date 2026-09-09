using BitCode.Framework.Platform.Workflow.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

/// <summary>
/// Publica una versión en borrador -- la operación sensible de referencia del módulo (checklist Fase 6,
/// requisito común 7): solo una versión publicada puede iniciar instancias
/// (<c>IniciarInstanciaCommandHandler</c>). Antes de publicar, valida la forma mínima del grafo (regla de
/// negocio esperada, <see cref="Result.Failure"/>, nunca una excepción): exactamente un estado inicial, al
/// menos un estado final, toda transición referencia estados que existen en la misma versión, todo estado
/// con <see cref="WorkflowState.RequiereTarea"/> declara <see cref="WorkflowState.AsignadoPorDefectoUserId"/>,
/// y todo estado con <see cref="WorkflowState.SlaMinutos"/> declara <see cref="WorkflowState.EscalarAUserId"/>
/// (sin esto último, <c>WorkflowEscalamientoJob</c> no sabría a quién reasignar la tarea vencida).
/// </summary>
internal sealed record PublicarWorkflowVersionCommand(Guid WorkflowVersionId) : ICommand, IIdempotentCommand;

internal sealed class PublicarWorkflowVersionCommandValidator : AbstractValidator<PublicarWorkflowVersionCommand>
{
    public PublicarWorkflowVersionCommandValidator() => RuleFor(c => c.WorkflowVersionId).NotEmpty();
}

internal sealed class PublicarWorkflowVersionCommandHandler(
    IRepository<WorkflowVersion, Guid> versionRepository,
    IReadRepository<WorkflowState, Guid> stateRepository,
    IReadRepository<WorkflowTransition, Guid> transitionRepository,
    IAuditWriter auditWriter,
    IWorkflowActorContext actorContext)
    : IRequestHandler<PublicarWorkflowVersionCommand, Result>
{
    public async Task<Result> Handle(PublicarWorkflowVersionCommand request, CancellationToken cancellationToken)
    {
        var version = await versionRepository.GetByIdAsync(request.WorkflowVersionId, cancellationToken);
        if (version is null)
        {
            return Result.Failure(Error.NotFound(
                "Workflow.Versiones.NoEncontrada", $"No existe la versión {request.WorkflowVersionId}."));
        }

        var estados = await stateRepository.ListAsync(new EstadosDeVersionSpecification(version.Id), cancellationToken);
        var transiciones = await transitionRepository.ListAsync(new TransicionesDeVersionSpecification(version.Id), cancellationToken);

        var validacion = ValidarGrafo(estados, transiciones);
        if (validacion.IsFailure)
        {
            return validacion;
        }

        var publicarResult = version.Publicar();
        if (publicarResult.IsFailure)
        {
            return publicarResult;
        }

        versionRepository.Update(version);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: version.TenantId == Guid.Empty ? null : version.TenantId,
            action: "workflow.versiones.publicar",
            resource: new AuditResource("workflow.versiones", version.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?>
            {
                ["workflowDefinitionId"] = version.WorkflowDefinitionId.ToString(),
                ["numero"] = version.Numero.ToString(),
            });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return Result.Success();
    }

    private static Result ValidarGrafo(IReadOnlyList<WorkflowState> estados, IReadOnlyList<WorkflowTransition> transiciones)
    {
        if (estados.Count(e => e.EsInicial) != 1)
        {
            return Result.Failure(Error.Validation(
                "Workflow.Versiones.GrafoInvalido", "El grafo debe tener exactamente un estado inicial."));
        }

        if (!estados.Any(e => e.EsFinal))
        {
            return Result.Failure(Error.Validation(
                "Workflow.Versiones.GrafoInvalido", "El grafo debe tener al menos un estado final."));
        }

        var idsDeEstados = estados.Select(e => e.Id).ToHashSet();
        if (transiciones.Any(t => !idsDeEstados.Contains(t.DesdeEstadoId) || !idsDeEstados.Contains(t.HaciaEstadoId)))
        {
            return Result.Failure(Error.Validation(
                "Workflow.Versiones.GrafoInvalido", "Hay una transición que referencia un estado que no pertenece a esta versión."));
        }

        if (estados.Any(e => e.RequiereTarea && e.AsignadoPorDefectoUserId is null))
        {
            return Result.Failure(Error.Validation(
                "Workflow.Versiones.GrafoInvalido", "Todo estado que requiere tarea debe declarar un asignado por defecto."));
        }

        if (estados.Any(e => e.SlaMinutos is not null && e.EscalarAUserId is null))
        {
            return Result.Failure(Error.Validation(
                "Workflow.Versiones.GrafoInvalido", "Todo estado con SLA debe declarar a quién escalar."));
        }

        return Result.Success();
    }
}
