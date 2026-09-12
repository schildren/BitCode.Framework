using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

/// <summary>
/// Conecta dos <see cref="WorkflowState"/> de la misma <see cref="WorkflowVersion"/> (Fase 6, módulo
/// Workflow) -- "pasos condicionales" del Plan Maestro: la transición solo es válida si
/// <see cref="ReglaExpresion"/> es nula (siempre aplica) o si evalúa a verdadero contra el diccionario de
/// variables de la instancia (<c>WorkflowRuleEvaluator.Evaluar</c>). Es un <see cref="Entity{TId}"/>
/// simple, mismo criterio que <see cref="WorkflowState"/>.
/// </summary>
public sealed class WorkflowTransition : Entity<Guid>, ITenantEntity
{
    public Guid WorkflowVersionId { get; private set; }

    public Guid DesdeEstadoId { get; private set; }

    public Guid HaciaEstadoId { get; private set; }

    /// <summary>Etiqueta de la acción que dispara esta transición (por ejemplo, "Aprobar", "Rechazar",
    /// "Avanzar") -- para una transición saliente de un estado con <c>RequiereTarea = true</c>, coincide
    /// con el <c>Accion</c> que <c>ResolverTareaCommand</c> recibe; para una transición saliente de un
    /// estado sin tarea (puramente automático/condicional), el motor evalúa TODAS las transiciones del
    /// estado sin filtrar por acción, en el orden de <see cref="Orden"/>, y toma la primera cuya
    /// <see cref="ReglaExpresion"/> aplique.</summary>
    public string Accion { get; private set; } = string.Empty;

    /// <summary>Expresión simple "{variable} {operador} {valor}" (operadores soportados: ==, !=, &gt;,
    /// &gt;=, &lt;, &lt;=) evaluada contra las variables de contexto de la instancia -- DELIBERADAMENTE no
    /// es un motor de reglas genérico (Plan Maestro, "Épica de Workflow": mantener la regla simple). Nula
    /// = la transición siempre aplica (sujeta solo a que coincida <see cref="Accion"/>).</summary>
    public string? ReglaExpresion { get; private set; }

    /// <summary>Orden de evaluación entre transiciones que comparten el mismo <see cref="DesdeEstadoId"/>
    /// -- desempata cuando más de una podría aplicar (primera por orden ascendente cuya regla evalúa a
    /// verdadero gana).</summary>
    public int Orden { get; private set; }

    public Guid TenantId { get; set; }

    public WorkflowTransition(
        Guid id, Guid workflowVersionId, Guid desdeEstadoId, Guid haciaEstadoId, string accion,
        string? reglaExpresion, int orden)
        : base(id)
    {
        WorkflowVersionId = workflowVersionId;
        DesdeEstadoId = desdeEstadoId;
        HaciaEstadoId = haciaEstadoId;
        Accion = accion;
        ReglaExpresion = reglaExpresion;
        Orden = orden;
    }

    private WorkflowTransition()
    {
    }
}
