using System.Collections.Concurrent;
using System.Reflection;
using BitCode.Framework.Shared.Domain.HotPaths;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.HotPaths;

/// <summary>
/// Implementación de <see cref="IHotPathQueryExecutor"/> registrada por <c>AddSharedPersistence</c>.
/// Traduce <see cref="IHotPathQuery{TResult}.ToSql"/> a <c>Database.SqlQuery&lt;TResult&gt;</c> de
/// EF Core (parametrizado igual que <c>FromSqlInterpolated</c>, nunca concatenación de strings) y
/// materializa el resultado antes de devolverlo — nunca expone el <c>IQueryable</c> intermedio que EF
/// Core construye internamente (regla dura #5 de <c>docs/convenciones.md</c>).
/// </summary>
public sealed class HotPathQueryExecutor(DbContext dbContext) : IHotPathQueryExecutor
{
    // Cachea, por tipo concreto de IHotPathQuery<>, que ya se validó el atributo [HotPath] — evitar
    // pagar el costo de reflexión en cada ejecución de una consulta que, por definición, es un hot
    // path (se ejecuta con alta frecuencia).
    private static readonly ConcurrentDictionary<Type, bool> ValidatedQueryTypes = new();

    public async Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        IHotPathQuery<TResult> query,
        CancellationToken cancellationToken = default)
        where TResult : class
    {
        EnsureJustified(query);

        return await dbContext.Database
            .SqlQuery<TResult>(query.ToSql())
            .ToListAsync(cancellationToken);
    }

    public async Task<TResult?> SingleOrDefaultAsync<TResult>(
        IHotPathQuery<TResult> query,
        CancellationToken cancellationToken = default)
        where TResult : class
    {
        EnsureJustified(query);

        return await dbContext.Database
            .SqlQuery<TResult>(query.ToSql())
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Exige que el tipo concreto de <paramref name="query"/> declare <see cref="HotPathAttribute"/>
    /// con <c>Justification</c> y <c>BenchmarkRef</c> no vacíos (criterio de aceptación de F1-18:
    /// "Benchmark justifica cada bypass"). Falla rápido con <see cref="InvalidOperationException"/> en
    /// vez de ejecutar una consulta especializada sin evidencia documentada de que el patrón genérico
    /// (<c>ISpecification&lt;T&gt;</c>) no alcanzaba. Ver <c>docs/guia-hot-paths.md</c>.
    /// </summary>
    private static void EnsureJustified<TResult>(IHotPathQuery<TResult> query)
        where TResult : class
    {
        var queryType = query.GetType();
        if (ValidatedQueryTypes.ContainsKey(queryType))
        {
            return;
        }

        var attribute = queryType.GetCustomAttribute<HotPathAttribute>();
        if (attribute is null)
        {
            throw new InvalidOperationException(
                $"'{queryType.FullName}' implementa IHotPathQuery<> pero no declara [HotPath(Justification, BenchmarkRef)]. " +
                "Ver docs/guia-hot-paths.md: todo bypass del patrón ISpecification<T> genérico debe " +
                "documentar por qué no alcanzaba y referenciar el benchmark que lo demuestra.");
        }

        if (string.IsNullOrWhiteSpace(attribute.Justification) || string.IsNullOrWhiteSpace(attribute.BenchmarkRef))
        {
            throw new InvalidOperationException(
                $"'{queryType.FullName}' declara [HotPath] pero con Justification o BenchmarkRef vacíos. " +
                "Ambos son obligatorios — ver docs/guia-hot-paths.md.");
        }

        ValidatedQueryTypes[queryType] = true;
    }
}
