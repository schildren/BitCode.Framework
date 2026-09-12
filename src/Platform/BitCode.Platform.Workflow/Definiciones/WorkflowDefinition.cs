using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

/// <summary>
/// Identidad estable de un workflow (Fase 6, módulo Workflow): el propio <see cref="WorkflowDefinition"/>
/// nunca cambia una vez creado (código y nombre descriptivo), toda evolución del grafo de estados vive en
/// una <see cref="WorkflowVersion"/> nueva -- mismo patrón que <c>Catalogo</c>/<c>CatalogoVersion</c>
/// (Fase 6, módulo 3, "Catálogos versionados"): una <see cref="Instancias.WorkflowInstance"/> ya en curso
/// queda atada a la <see cref="WorkflowVersion"/> con la que arrancó, así que publicar una versión nueva
/// del mismo workflow nunca afecta instancias existentes (ver <c>docs/guia-workflow.md</c>, sección
/// "Modelo de versionado"). Es un <see cref="AggregateRoot{TId}"/> propio del módulo: no reutiliza tipos
/// ajenos de otro bounded context.
/// </summary>
public sealed class WorkflowDefinition : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, ISoftDelete
{
    /// <summary>Código lógico estable del workflow (por ejemplo, "APROBACION_GASTOS") -- único dentro del
    /// tenant, ver el índice compuesto <c>(TenantId, Codigo)</c> en <see cref="WorkflowDbContext"/>.</summary>
    public string Codigo { get; private set; } = string.Empty;

    public string Nombre { get; private set; } = string.Empty;

    public string? Descripcion { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }

    public WorkflowDefinition(Guid id, string codigo, string nombre, string? descripcion) : base(id)
    {
        Codigo = codigo;
        Nombre = nombre;
        Descripcion = descripcion;
    }

    private WorkflowDefinition()
    {
    }
}
