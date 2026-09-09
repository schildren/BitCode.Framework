using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Catalogs.Catalogos;

/// <summary>
/// Un valor concreto dentro de una <see cref="CatalogoVersion"/> (Fase 6, módulo Catalogs and
/// Parameters) -- por ejemplo, dentro de la versión 3 del catálogo "TIPOS_DOCUMENTO", el ítem
/// <c>Codigo = "DNI"</c>. Es un <see cref="Entity{TId}"/> simple (no <see cref="AggregateRoot{TId}"/>):
/// no levanta eventos propios, su ciclo de vida está atado por completo a la versión que lo contiene y
/// no tiene sentido de negocio fuera de ella -- pero conserva <see cref="ITenantEntity"/> porque tiene su
/// propia tabla/DbSet con filtro global de tenant, accedida vía su propio
/// <c>IRepository&lt;CatalogoItem, Guid&gt;</c> (regla dura 1, nunca vía navegación de
/// <see cref="CatalogoVersion"/>).
/// </summary>
public sealed class CatalogoItem : Entity<Guid>, ITenantEntity
{
    public Guid CatalogoVersionId { get; private set; }

    public string Codigo { get; private set; } = string.Empty;

    public string Etiqueta { get; private set; } = string.Empty;

    public string? Valor { get; private set; }

    public int Orden { get; private set; }

    public Guid TenantId { get; set; }

    public CatalogoItem(Guid id, Guid catalogoVersionId, string codigo, string etiqueta, string? valor, int orden) : base(id)
    {
        CatalogoVersionId = catalogoVersionId;
        Codigo = codigo;
        Etiqueta = etiqueta;
        Valor = valor;
        Orden = orden;
    }

    private CatalogoItem()
    {
    }
}
