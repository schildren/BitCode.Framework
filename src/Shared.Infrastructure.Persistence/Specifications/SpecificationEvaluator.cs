using BitCode.Framework.Shared.Domain.Specifications;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Specifications;

public static class SpecificationEvaluator<T> where T : class
{
    public static IQueryable<T> GetQuery(IQueryable<T> inputQuery, ISpecification<T> specification) =>
        GetQuery(inputQuery, specification, applyPaging: true);

    /// <remarks>
    /// El overload con <paramref name="applyPaging"/> existe para que un repositorio pueda construir,
    /// a partir de la misma especificación (mismo Criteria/Includes/OrderBy), tanto la consulta de
    /// conteo total (sin Skip/Take, para <c>PagedResult&lt;T&gt;.TotalCount</c>) como la consulta de la
    /// página solicitada — sin duplicar la lógica de filtrado en dos lugares (F1-17).
    /// </remarks>
    public static IQueryable<T> GetQuery(IQueryable<T> inputQuery, ISpecification<T> specification, bool applyPaging)
    {
        var query = inputQuery;

        if (specification.Criteria is not null)
        {
            query = query.Where(specification.Criteria);
        }

        query = specification.Includes.Aggregate(query, (current, include) => current.Include(include));
        query = specification.IncludeStrings.Aggregate(query, (current, include) => current.Include(include));

        if (specification.OrderBy is not null)
        {
            query = query.OrderBy(specification.OrderBy);
        }
        else if (specification.OrderByDescending is not null)
        {
            query = query.OrderByDescending(specification.OrderByDescending);
        }

        if (specification.GroupBy is not null)
        {
            query = query.GroupBy(specification.GroupBy).SelectMany(g => g);
        }

        if (applyPaging && specification.IsPagingEnabled)
        {
            query = query.Skip(specification.Skip).Take(specification.Take);
        }

        if (specification.AsNoTracking)
        {
            query = query.AsNoTracking();
        }

        return query;
    }
}
