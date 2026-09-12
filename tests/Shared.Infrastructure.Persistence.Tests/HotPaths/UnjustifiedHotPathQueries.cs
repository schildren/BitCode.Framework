using BitCode.Framework.Shared.Domain.HotPaths;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.HotPaths;

/// <summary>
/// Implementa <see cref="IHotPathQuery{TResult}"/> sin el atributo <see cref="HotPathAttribute"/> — usada
/// solo para probar que <c>HotPathQueryExecutor</c> rechaza ejecutar un hot path sin justificación
/// documentada (criterio de aceptación de F1-18: "Benchmark justifica cada bypass").
/// </summary>
public sealed class MissingAttributeHotPathQuery : IHotPathQuery<ResumenAmountResult>
{
    public FormattableString ToSql() => $"SELECT COUNT(*) AS Total, 0 AS SumaAmount FROM TestEntities";
}

/// <summary>
/// Implementa <see cref="IHotPathQuery{TResult}"/> con <see cref="HotPathAttribute"/> pero con
/// <c>Justification</c>/<c>BenchmarkRef</c> vacíos — mismo propósito de prueba negativa que
/// <see cref="MissingAttributeHotPathQuery"/>.
/// </summary>
[HotPath(justification: "", benchmarkRef: "")]
public sealed class EmptyAttributeHotPathQuery : IHotPathQuery<ResumenAmountResult>
{
    public FormattableString ToSql() => $"SELECT COUNT(*) AS Total, 0 AS SumaAmount FROM TestEntities";
}
