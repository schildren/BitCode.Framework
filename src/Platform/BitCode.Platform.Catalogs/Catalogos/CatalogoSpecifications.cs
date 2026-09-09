using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.Catalogs.Catalogos;

/// <summary>Especificaciones reutilizadas por más de un handler de <c>Catalogos</c> -- agrupadas en un
/// único archivo para evitar duplicar el mismo criterio de filtro entre comandos/queries (regla dura 5,
/// nunca <c>IQueryable</c> expuesto: toda consulta pasa por una <see cref="Specification{T}"/>).</summary>
internal sealed class CatalogoPorCodigoSpecification : Specification<Catalogo>
{
    public CatalogoPorCodigoSpecification(string codigo) => ApplyCriteria(c => c.Codigo == codigo);
}

internal sealed class TodosLosCatalogosOrdenadosPorCodigoSpecification : Specification<Catalogo>
{
    public TodosLosCatalogosOrdenadosPorCodigoSpecification() => ApplyOrderBy(c => c.Codigo);
}

internal sealed class VersionesDeCatalogoSpecification : Specification<CatalogoVersion>
{
    public VersionesDeCatalogoSpecification(Guid catalogoId) => ApplyCriteria(v => v.CatalogoId == catalogoId);
}

/// <summary>Versión vigente de un catálogo en una fecha dada: publicada, con
/// <c>VigenteDesde &lt;= fecha</c> y (<c>VigenteHasta</c> nula o posterior a <c>fecha</c>) -- por
/// construcción (ver <see cref="CatalogoVersion.CerrarVigencia"/>, llamado al publicar la versión
/// siguiente) nunca hay más de una fila que cumpla este criterio para el mismo catálogo.</summary>
internal sealed class CatalogoVersionVigenteEnFechaSpecification : Specification<CatalogoVersion>
{
    public CatalogoVersionVigenteEnFechaSpecification(Guid catalogoId, DateTime fecha) => ApplyCriteria(v =>
        v.CatalogoId == catalogoId &&
        v.Estado == CatalogoVersionEstado.Publicada &&
        v.VigenteDesde <= fecha &&
        (v.VigenteHasta == null || v.VigenteHasta > fecha));
}

/// <summary>La versión actualmente vigente (sin fecha de cierre todavía) de un catálogo -- la que hay
/// que cerrar (<see cref="CatalogoVersion.CerrarVigencia"/>) al publicar una versión nueva.</summary>
internal sealed class CatalogoVersionVigenteAbiertaSpecification : Specification<CatalogoVersion>
{
    public CatalogoVersionVigenteAbiertaSpecification(Guid catalogoId) => ApplyCriteria(v =>
        v.CatalogoId == catalogoId &&
        v.Estado == CatalogoVersionEstado.Publicada &&
        v.VigenteHasta == null);
}

internal sealed class ItemsDeVersionOrdenadosSpecification : Specification<CatalogoItem>
{
    public ItemsDeVersionOrdenadosSpecification(Guid catalogoVersionId)
    {
        ApplyCriteria(i => i.CatalogoVersionId == catalogoVersionId);
        ApplyOrderBy(i => i.Orden);
    }
}
