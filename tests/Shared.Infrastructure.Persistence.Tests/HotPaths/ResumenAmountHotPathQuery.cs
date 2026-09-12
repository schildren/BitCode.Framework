using BitCode.Framework.Shared.Domain.HotPaths;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.HotPaths;

/// <summary>
/// Resultado materializado de <see cref="ResumenAmountHotPathQuery"/>: un DTO plano, nunca la entidad
/// <see cref="TestEntity"/> completa ni un <c>IQueryable</c> — coherente con la regla dura #5 de
/// <c>docs/convenciones.md</c>.
/// </summary>
public sealed class ResumenAmountResult
{
    public int Total { get; set; }

    public long SumaAmount { get; set; }
}

/// <summary>
/// Ejemplo demostrativo de F1-18 (ver <c>docs/guia-hot-paths.md</c>): un resumen (cantidad de filas +
/// suma de <c>Amount</c>) que cumplen un filtro, calculado en un único <c>SELECT COUNT/SUM</c> contra
/// la base de datos. El camino genérico (<c>ISpecification&lt;T&gt;</c>/<c>IReadRepository&lt;,&gt;</c>,
/// F1-17) no expone ningún método de agregación (<c>CountAsync</c> cuenta, pero no suma) — la única
/// forma de obtener la suma con el contrato genérico es traer todas las filas filtradas con
/// <c>ListAsync(spec, e =&gt; e.Amount)</c> y sumarlas en memoria, transfiriendo N filas para un
/// resultado de una sola fila. Ver el benchmark real en
/// <c>HotPathBenchmarkTests.Benchmark_GenericSpecificationPath_Vs_HotPath</c> (docs/guia-hot-paths.md).
/// </summary>
[HotPath(
    justification: "El camino genérico (ISpecification/IReadRepository, F1-17) no tiene ningún método " +
        "de agregación: CountAsync cuenta filas, pero sumar Amount exige materializar todas las filas " +
        "filtradas con ListAsync(spec, selector) y sumarlas en memoria — para un resumen de dashboard " +
        "sobre una tabla grande esto transfiere N filas para producir 1. Un único SELECT COUNT/SUM " +
        "calcula el agregado en la base de datos y transfiere una sola fila.",
    benchmarkRef: "docs/guia-hot-paths.md#benchmark-resumen-de-agregados")]
public sealed class ResumenAmountHotPathQuery(int threshold) : IHotPathQuery<ResumenAmountResult>
{
    public FormattableString ToSql() =>
        $"""
         SELECT COUNT(*) AS Total, COALESCE(SUM(Amount), 0) AS SumaAmount
         FROM TestEntities
         WHERE Amount > {threshold} AND IsDeleted = 0
         """;
}
