using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.Catalogs.Parametros;

/// <summary>Especificaciones reutilizadas por más de un handler de <c>Parametros</c> -- agrupadas en un
/// único archivo (mismo criterio que <c>CatalogoSpecifications</c>) para no duplicar el mismo criterio
/// de filtro entre comandos/queries (regla dura 5, nunca <c>IQueryable</c> expuesto).</summary>
internal sealed class ParametroPorCodigoSpecification : Specification<Parametro>
{
    public ParametroPorCodigoSpecification(string codigo) => ApplyCriteria(p => p.Codigo == codigo);
}

internal sealed class TodosLosParametrosOrdenadosPorCodigoSpecification : Specification<Parametro>
{
    public TodosLosParametrosOrdenadosPorCodigoSpecification() => ApplyOrderBy(p => p.Codigo);
}

internal sealed class VigenciasDeParametroOrdenadasSpecification : Specification<ParametroVigencia>
{
    public VigenciasDeParametroOrdenadasSpecification(Guid parametroId)
    {
        ApplyCriteria(v => v.ParametroId == parametroId);
        ApplyOrderBy(v => v.VigenteDesde);
    }
}

/// <summary>
/// Vigencias del mismo parámetro que se solaparían con el rango <c>[vigenteDesde, vigenteHasta)</c>
/// propuesto -- dos vigencias se solapan si cada una empieza antes de que la otra termine. Usada por
/// <c>CrearParametroVigenciaCommandHandler</c> para rechazar el alta como <c>Result.Failure</c> (error
/// de negocio esperado, nunca una excepción ni una restricción de base de datos) ANTES de persistir.
/// </summary>
internal sealed class VigenciasSuperpuestasSpecification : Specification<ParametroVigencia>
{
    public VigenciasSuperpuestasSpecification(Guid parametroId, DateTime vigenteDesde, DateTime? vigenteHasta)
    {
        var finPropuesto = vigenteHasta ?? DateTime.MaxValue;
        ApplyCriteria(v =>
            v.ParametroId == parametroId &&
            v.VigenteDesde < finPropuesto &&
            (v.VigenteHasta == null || v.VigenteHasta > vigenteDesde));
    }
}

/// <summary>La vigencia activa de un parámetro en una fecha dada -- por construcción (validado al
/// crear cada vigencia mediante <see cref="VigenciasSuperpuestasSpecification"/>) nunca hay más de una
/// fila que cumpla este criterio para el mismo parámetro.</summary>
internal sealed class VigenciaEnFechaSpecification : Specification<ParametroVigencia>
{
    public VigenciaEnFechaSpecification(Guid parametroId, DateTime fecha) => ApplyCriteria(v =>
        v.ParametroId == parametroId &&
        v.VigenteDesde <= fecha &&
        (v.VigenteHasta == null || v.VigenteHasta > fecha));
}
