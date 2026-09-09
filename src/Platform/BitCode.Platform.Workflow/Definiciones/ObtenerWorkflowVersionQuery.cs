using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

/// <summary>Devuelve el grafo completo (estados + transiciones) de una versión -- la consulta que un
/// consumidor real usa para renderizar/inspeccionar el workflow antes de iniciar una instancia.</summary>
internal sealed record ObtenerWorkflowVersionQuery(Guid Id) : IQuery<WorkflowVersionResponse>;

internal sealed class ObtenerWorkflowVersionQueryHandler(
    IReadRepository<WorkflowVersion, Guid> versionRepository,
    IReadRepository<WorkflowState, Guid> stateRepository,
    IReadRepository<WorkflowTransition, Guid> transitionRepository)
    : IRequestHandler<ObtenerWorkflowVersionQuery, Result<WorkflowVersionResponse>>
{
    public async Task<Result<WorkflowVersionResponse>> Handle(ObtenerWorkflowVersionQuery request, CancellationToken cancellationToken)
    {
        var version = await versionRepository.GetByIdAsync(request.Id, cancellationToken);
        if (version is null)
        {
            return Result.Failure<WorkflowVersionResponse>(
                Error.NotFound("Workflow.Versiones.NoEncontrada", $"No existe la versión {request.Id}."));
        }

        var estados = await stateRepository.ListAsync(new EstadosDeVersionSpecification(version.Id), cancellationToken);
        var transiciones = await transitionRepository.ListAsync(new TransicionesDeVersionSpecification(version.Id), cancellationToken);

        return new WorkflowVersionResponse(
            version.Id, version.WorkflowDefinitionId, version.Numero, version.Estado, version.PublicadaAtUtc,
            [.. estados.Select(e => new WorkflowStateResponse(
                e.Id, e.Codigo, e.Nombre, e.EsInicial, e.EsFinal, e.RequiereTarea, e.TituloTarea,
                e.AsignadoPorDefectoUserId, e.SlaMinutos, e.EscalarAUserId))],
            [.. transiciones.Select(t => new WorkflowTransitionResponse(
                t.Id, t.DesdeEstadoId, t.HaciaEstadoId, t.Accion, t.ReglaExpresion, t.Orden))]);
    }
}
