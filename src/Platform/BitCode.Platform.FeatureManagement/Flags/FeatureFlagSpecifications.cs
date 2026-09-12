using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.FeatureManagement.Flags;

/// <summary>Especificaciones reutilizadas por más de un handler de <c>Flags</c> -- agrupadas en un único
/// archivo para evitar duplicar el mismo criterio de filtro entre comandos/queries (regla dura 5, nunca
/// <c>IQueryable</c> expuesto: toda consulta pasa por una <see cref="Specification{T}"/>).</summary>
internal sealed class FeatureFlagPorNombreSpecification : Specification<FeatureFlag>
{
    public FeatureFlagPorNombreSpecification(string nombre) => ApplyCriteria(f => f.Nombre == nombre);
}

internal sealed class TodosLosFlagsOrdenadosPorNombreSpecification : Specification<FeatureFlag>
{
    public TodosLosFlagsOrdenadosPorNombreSpecification() => ApplyOrderBy(f => f.Nombre);
}
