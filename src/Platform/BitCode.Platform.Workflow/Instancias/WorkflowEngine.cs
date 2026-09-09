using BitCode.Framework.Platform.Workflow.Definiciones;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>
/// Motor de avance de una <see cref="WorkflowInstance"/> -- lógica compartida entre
/// <c>IniciarInstanciaCommandHandler</c> (primer avance, sin acción resuelta todavía) y
/// <c>ResolverTareaCommandHandler</c> (avance disparado por una aprobación/rechazo), para no duplicar el
/// algoritmo "buscar la transición aplicable y moverse" en dos handlers. No es un
/// <see cref="MediatR.IRequestHandler{TRequest}"/> -- es un servicio de aplicación interno inyectado
/// directamente por los handlers, dentro del mismo scope/transacción que ya abrió <c>TransactionBehavior</c>
/// (regla dura 1, docs/convenciones.md: nunca llama <c>IUnitOfWork.SaveChangesAsync</c> por su cuenta).
/// </summary>
internal interface IWorkflowEngine
{
    /// <summary>
    /// Busca, entre las transiciones salientes del estado actual de <paramref name="instancia"/>, la
    /// primera cuya <see cref="WorkflowTransition.ReglaExpresion"/> evalúe a verdadero contra las
    /// variables de la instancia -- filtrando además por <paramref name="accion"/> cuando se provee (el
    /// caso de una tarea resuelta: solo importan las transiciones etiquetadas con esa acción). Si
    /// encuentra una, mueve la instancia (<see cref="WorkflowInstance.AvanzarA"/>), escribe
    /// <see cref="WorkflowHistorial"/> y, si el estado destino requiere tarea humana, crea la
    /// <see cref="WorkflowTask"/> correspondiente (también con su propia fila de historial). Si el estado
    /// destino no requiere tarea y no es final, sigue avanzando automáticamente (recursivo, acotado por la
    /// ausencia de ciclos infinitos en un grafo bien formado -- <c>PublicarWorkflowVersionCommandHandler</c>
    /// no valida ausencia de ciclos en este primer alcance, ver <c>docs/guia-workflow.md</c>, sección
    /// "Pendientes"; un grafo con un ciclo sin condición de salida agotaría la pila, comportamiento
    /// aceptado y documentado, no protegido en este primer corte).
    /// </summary>
    Task<Result> AvanzarAsync(WorkflowInstance instancia, string? accion, CancellationToken cancellationToken);
}

internal sealed class WorkflowEngine(
    IReadRepository<WorkflowTransition, Guid> transitionRepository,
    IReadRepository<WorkflowState, Guid> stateRepository,
    IRepository<WorkflowTask, Guid> taskRepository,
    IRepository<WorkflowHistorial, Guid> historialRepository)
    : IWorkflowEngine
{
    public async Task<Result> AvanzarAsync(WorkflowInstance instancia, string? accion, CancellationToken cancellationToken)
    {
        if (instancia.Estado == WorkflowInstanceEstado.Finalizada)
        {
            return Result.Failure(Error.Conflict(
                "Workflow.Instancias.Finalizada", "La instancia ya alcanzó un estado final."));
        }

        var candidatas = await transitionRepository.ListAsync(
            new TransicionesDesdeEstadoSpecification(instancia.EstadoActualId), cancellationToken);

        if (accion is not null)
        {
            candidatas = [.. candidatas.Where(t => string.Equals(t.Accion, accion, StringComparison.OrdinalIgnoreCase))];
        }

        var variables = instancia.ObtenerVariables();
        var transicion = candidatas.FirstOrDefault(t => WorkflowRuleEvaluator.Evaluar(t.ReglaExpresion, variables));

        if (transicion is null)
        {
            return Result.Failure(Error.Conflict(
                "Workflow.Instancias.SinTransicionAplicable",
                accion is null
                    ? "No hay ninguna transición automática aplicable desde el estado actual."
                    : $"No hay ninguna transición para la acción '{accion}' aplicable desde el estado actual."));
        }

        var estadoDestino = await stateRepository.GetByIdAsync(transicion.HaciaEstadoId, cancellationToken);
        if (estadoDestino is null)
        {
            return Result.Failure(Error.NotFound(
                "Workflow.Instancias.EstadoDestinoNoEncontrado", "El estado destino de la transición no existe."));
        }

        instancia.AvanzarA(estadoDestino.Id, estadoDestino.EsFinal);

        await historialRepository.AddAsync(
            new WorkflowHistorial(
                Guid.NewGuid(), instancia.Id,
                estadoDestino.EsFinal ? "InstanciaFinalizada" : "InstanciaAvanzada",
                $"Transición '{transicion.Accion}' -> estado '{estadoDestino.Nombre}'.",
                actorUserId: null),
            cancellationToken);

        if (estadoDestino.EsFinal)
        {
            return Result.Success();
        }

        if (estadoDestino.RequiereTarea)
        {
            var tarea = new WorkflowTask(
                Guid.NewGuid(), instancia.Id, estadoDestino.Id,
                estadoDestino.TituloTarea ?? estadoDestino.Nombre,
                estadoDestino.AsignadoPorDefectoUserId!.Value,
                estadoDestino.SlaMinutos.HasValue ? DateTime.UtcNow.AddMinutes(estadoDestino.SlaMinutos.Value) : null);
            await taskRepository.AddAsync(tarea, cancellationToken);

            await historialRepository.AddAsync(
                new WorkflowHistorial(
                    Guid.NewGuid(), instancia.Id, "TareaCreada",
                    $"Tarea '{tarea.Titulo}' asignada.", actorUserId: null),
                cancellationToken);

            return Result.Success();
        }

        // Estado intermedio puramente automático/condicional (sin tarea humana, "pasos paralelos"/
        // "condicionales" no aplican todavía): el motor sigue avanzando sin esperar ninguna acción externa.
        return await AvanzarAsync(instancia, accion: null, cancellationToken);
    }
}
