using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

/// <summary>Estado de una <see cref="WorkflowVersion"/>: nace en <see cref="Borrador"/> (el grafo de
/// <see cref="WorkflowState"/>/<see cref="WorkflowTransition"/> todavía se puede cargar) y pasa a
/// <see cref="Publicada"/> de forma irreversible al llamar <see cref="WorkflowVersion.Publicar"/> -- solo
/// una versión <see cref="Publicada"/> puede iniciar instancias nuevas.</summary>
public enum WorkflowVersionEstado
{
    Borrador = 0,
    Publicada = 1,
}

/// <summary>
/// Una versión concreta y numerada del grafo de estados de un <see cref="WorkflowDefinition"/> (Fase 6,
/// módulo Workflow): agrupa los <see cref="WorkflowState"/>/<see cref="WorkflowTransition"/> vigentes para
/// esa versión. A diferencia de <c>CatalogoVersion</c> (Catalogs, Fase 6 módulo 3), publicar una versión
/// nueva NO cierra ninguna "vigencia" de la anterior: ambas pueden convivir publicadas al mismo tiempo,
/// porque cada <see cref="Instancias.WorkflowInstance"/> ya en curso queda atada para siempre a la versión
/// concreta con la que arrancó (nunca "la versión vigente al momento de consultar", como sí ocurre en
/// Catalogs) -- iniciar una instancia nueva simplemente toma la versión publicada más reciente del
/// workflow, ver <c>IniciarInstanciaCommandHandler</c>. Es un <see cref="AggregateRoot{TId}"/> propio (no
/// anidado dentro de <see cref="WorkflowDefinition"/>) porque su publicación es una operación sensible de
/// referencia del módulo (checklist Fase 6, requisito común 7).
/// </summary>
public sealed class WorkflowVersion : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity
{
    public Guid WorkflowDefinitionId { get; private set; }

    /// <summary>Número incremental dentro del workflow (1, 2, 3, ...) -- deliberadamente NO semver
    /// completo, mismo criterio que <c>CatalogoVersion.Numero</c>.</summary>
    public int Numero { get; private set; }

    public WorkflowVersionEstado Estado { get; private set; } = WorkflowVersionEstado.Borrador;

    public DateTime? PublicadaAtUtc { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public WorkflowVersion(Guid id, Guid workflowDefinitionId, int numero) : base(id)
    {
        WorkflowDefinitionId = workflowDefinitionId;
        Numero = numero;
    }

    private WorkflowVersion()
    {
    }

    /// <summary>
    /// Publica el grafo en borrador -- irreversible (nunca vuelve a <see cref="WorkflowVersionEstado.Borrador"/>).
    /// La validación estructural del grafo (exactamente un estado inicial, al menos un estado final,
    /// destino de cada transición existente) ocurre ANTES de llamar este método, en
    /// <c>PublicarWorkflowVersionCommandHandler</c> (necesita leer <see cref="WorkflowState"/>/
    /// <see cref="WorkflowTransition"/> vía repositorio propio, regla dura 1 -- este agregado no navega a
    /// sus hijos).
    /// </summary>
    public Result Publicar()
    {
        if (Estado == WorkflowVersionEstado.Publicada)
        {
            return Result.Failure(Error.Conflict(
                "Workflow.Versiones.YaPublicada", "La versión ya fue publicada anteriormente."));
        }

        Estado = WorkflowVersionEstado.Publicada;
        PublicadaAtUtc = DateTime.UtcNow;

        RaiseDomainEvent(new WorkflowVersionPublicadaIntegrationEvent(Id, WorkflowDefinitionId, Numero));

        return Result.Success();
    }
}
