using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.FeatureManagement.Segmentos;

internal sealed class TodosLosSegmentosOrdenadosPorNombreSpecification : Specification<Segmento>
{
    public TodosLosSegmentosOrdenadosPorNombreSpecification() => ApplyOrderBy(s => s.Nombre);
}

/// <summary>Todos los segmentos cuyo <c>Id</c> está en el conjunto dado -- usada por
/// <c>EvaluarFeatureFlagQueryHandler</c> para resolver, en una sola consulta, los segmentos asociados a
/// los <see cref="Rollouts.Rollout"/> de un flag (regla dura 5: nunca <c>IQueryable</c> expuesto).</summary>
internal sealed class SegmentosPorIdsSpecification : Specification<Segmento>
{
    public SegmentosPorIdsSpecification(IReadOnlyCollection<Guid> ids) => ApplyCriteria(s => ids.Contains(s.Id));
}
