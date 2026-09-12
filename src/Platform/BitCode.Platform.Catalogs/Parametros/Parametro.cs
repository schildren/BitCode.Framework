using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Catalogs.Parametros;

/// <summary>
/// Identidad estable de un parámetro configurable con vigencia (Fase 6, módulo Catalogs and
/// Parameters) -- por ejemplo, "TasaIVA" o "LimiteDiarioTransferencia". El propio <see cref="Parametro"/>
/// nunca guarda un valor: el valor vigente en un momento dado se resuelve consultando sus
/// <see cref="ParametroVigencia"/> (ver <c>ObtenerValorVigenteQuery</c>). Es un
/// <see cref="AggregateRoot{TId}"/> propio del módulo, mismo criterio que <see cref="Catalogos.Catalogo"/>.
/// </summary>
public sealed class Parametro : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, ISoftDelete
{
    /// <summary>Código lógico estable del parámetro -- único dentro del tenant, ver el índice compuesto
    /// <c>(TenantId, Codigo)</c> en <see cref="CatalogsDbContext"/>.</summary>
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

    public Parametro(Guid id, string codigo, string nombre, string? descripcion) : base(id)
    {
        Codigo = codigo;
        Nombre = nombre;
        Descripcion = descripcion;
    }

    private Parametro()
    {
    }
}
