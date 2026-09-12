using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Catalogs.Catalogos;

/// <summary>
/// Identidad estable de un catálogo versionado (Fase 6, módulo Catalogs and Parameters): el propio
/// <see cref="Catalogo"/> nunca cambia una vez creado (código y nombre descriptivo), toda evolución del
/// contenido vive en una <see cref="CatalogoVersion"/> nueva -- una referencia externa a una versión
/// concreta (por ejemplo, otro módulo que guardó el <c>CatalogoVersionId</c> vigente al momento de leerlo)
/// nunca deja de resolver, aunque se publique una versión más nueva (ver
/// <c>docs/guia-catalogs.md</c>, sección "Modelo de versionado"). Es un <see cref="AggregateRoot{TId}"/>
/// propio del módulo (mismo criterio que <c>Empresa</c> en Organization, Fase 6 módulo 2): no reutiliza
/// tipos ajenos de otro bounded context.
/// </summary>
public sealed class Catalogo : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, ISoftDelete
{
    /// <summary>Código lógico estable del catálogo (por ejemplo, "TIPOS_DOCUMENTO", "MONEDAS") -- único
    /// dentro del tenant, ver el índice compuesto <c>(TenantId, Codigo)</c> en <see cref="CatalogsDbContext"/>.</summary>
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

    public Catalogo(Guid id, string codigo, string nombre, string? descripcion) : base(id)
    {
        Codigo = codigo;
        Nombre = nombre;
        Descripcion = descripcion;
    }

    private Catalogo()
    {
    }
}
